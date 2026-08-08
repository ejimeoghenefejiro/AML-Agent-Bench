using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Export;
using AmlAgent.Adapters.Manifest;
using AmlAgent.Oracle;

namespace AmlAgent.Harness;

/// <summary>
/// Language-agnostic Docker-based benchmark runner. The agent under test can
/// come from three sources, in priority order:
///
///   --agent-image &lt;tag&gt;      a pre-built Docker image (no build step)
///   --submission   &lt;path&gt;     a local folder containing a Dockerfile
///   --agent        &lt;name&gt;     a subfolder of agents/ in the repo (default)
///
/// The harness stages a temp workspace from tasks/&lt;task&gt;/environment/ +
/// the task's instruction.md/prompt.md, runs the agent container against /app,
/// and then evaluates the workspace with:
///
///   1. xUnit (AmlAgent.Tests) — structural / deterministic assertions
///   2. aml-agent judge — LLM-as-judge rubric scoring, if the task has rubric.json
///
/// Either evaluator failing causes a non-zero overall exit code, but both are
/// always attempted so users see the full picture.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        // Subcommands (CLI-Only Assurance Roadmap items 9/10) operate on
        // already-produced assurance_profile.json files and don't need
        // .env / OPENAI_API_KEY at all, so they're dispatched before the
        // normal run flow's setup.
        if (args.Length > 0 && args[0] == "compare")
            return CompareCommand.Run(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0] == "regress")
            return RegressCommand.Run(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0] == "load-dataset")
            return LoadDatasetCommand.Run(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0] == "load-case")
            return LoadCaseCommand.Run(args.Skip(1).ToArray());

        var envFile = DotEnv.Load();
        if (envFile is not null)
            Console.WriteLine($"[env] loaded {envFile}");

        string agent = "csharp-sk";
        string task = "aml-transaction-network";
        string? model = null;
        string? agentImage = null;
        string? submission = null;
        int maxSteps = 25;
        bool keepWorkspace = false;
        bool useOracle = false;
        bool skipJudge = false;
        bool useLocal = false;
        string? policyPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--agent"        when i + 1 < args.Length: agent = args[++i]; break;
                case "--agent-image"  when i + 1 < args.Length: agentImage = args[++i]; break;
                case "--submission"   when i + 1 < args.Length: submission = args[++i]; break;
                case "--task"         when i + 1 < args.Length: task = args[++i]; break;
                case "--model"        when i + 1 < args.Length: model = args[++i]; break;
                case "--max-steps"    when i + 1 < args.Length: maxSteps = int.Parse(args[++i]); break;
                case "--policy"       when i + 1 < args.Length: policyPath = args[++i]; break;
                case "--keep-workspace": keepWorkspace = true; break;
                case "--oracle":         useOracle = true; break;
                case "--no-judge":       skipJudge = true; break;
                case "--local":          useLocal = true; break;
                case "-h" or "--help":   PrintUsage(); return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return 64;
            }
        }

        var repoRoot = FindRepoRoot()
            ?? throw new InvalidOperationException("Could not locate repo root (looking for AML-Agent-Bench.sln)");

        var taskDir = ResolveTaskDir(repoRoot, task);
        if (taskDir is null) { Console.Error.WriteLine($"task not found: tasks/{task} (or any unique prefix match)"); return 1; }
        task = Path.GetFileName(taskDir)!; // normalise to the canonical full task id for everything downstream

        var runId = Guid.NewGuid().ToString("N");
        var startedUtc = DateTime.UtcNow;
        var workspace = Path.Combine(Path.GetTempPath(), $"aml-bench-{task}-{runId}");
        Directory.CreateDirectory(workspace);
        try
        {
            StageWorkspace(taskDir, workspace);
            Console.WriteLine($"[harness] task     = {task}");
            Console.WriteLine($"[harness] workspace = {workspace}");

            StageCanonicalCaseIfPresent(workspace);

            int agentRc;
            if (useOracle)
            {
                Console.WriteLine("[harness] --oracle: producing output via AmlAgent.Oracle (skipping agent container)");
                if (task != "aml-transaction-network")
                {
                    Console.Error.WriteLine("[harness] --oracle is only implemented for aml-transaction-network");
                    return 1;
                }
                var input = Path.Combine(workspace, "data", "transfers.csv");
                var output = Path.Combine(workspace, "aml_clusters.csv");
                var res = OracleRunner.Run(input, output);
                Console.WriteLine($"[harness] oracle wrote {res.ClustersWritten} clusters");
                agentRc = 0;
            }
            else if (useLocal)
            {
                _ = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? throw new InvalidOperationException("OPENAI_API_KEY not set");
                if (!string.IsNullOrEmpty(agentImage) || !string.IsNullOrEmpty(submission))
                {
                    Console.Error.WriteLine("[harness] --local is only valid for --agent <name> (in-repo agents); --agent-image and --submission require Docker.");
                    return 1;
                }
                Console.WriteLine($"[harness] --local: running agent directly via dotnet run (no Docker)");
                agentRc = RunAgentLocal(repoRoot, agent, workspace, model, maxSteps);
                Console.WriteLine($"[harness] agent exit code: {agentRc}");
            }
            else
            {
                var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? throw new InvalidOperationException("OPENAI_API_KEY not set");

                string image = ResolveAgentImage(repoRoot, agent, agentImage, submission);
                agentRc = RunAgentContainer(image, workspace, apiKey, model, maxSteps);
                Console.WriteLine($"[harness] agent exit code: {agentRc}");
            }

            // 1) Judge rubric first (if present) so the resulting
            //    judge_report.json is on disk before xUnit runs — otherwise
            //    JudgeReportTests skip themselves and we lose 4 assertions.
            int judgeRc = 0;
            var rubricPath = Path.Combine(taskDir, "rubric.json");
            if (!skipJudge && File.Exists(rubricPath))
            {
                Console.WriteLine($"\n[harness] running aml-agent judge against workspace");
                judgeRc = RunJudge(repoRoot, task, workspace);
                Console.WriteLine($"[harness] judge exit code: {judgeRc}");
            }
            else if (skipJudge)
            {
                Console.WriteLine("\n[harness] --no-judge: skipping LLM judge");
            }
            else
            {
                Console.WriteLine("\n[harness] no rubric.json for this task — judge stage skipped");
            }

            // 2) xUnit structural tests — runs LAST so it can assert on both the
            //    agent's outputs and the judge_report.json produced above.
            var testsProj = Path.Combine(repoRoot, "tests", "AmlAgent.Tests", "AmlAgent.Tests.csproj");
            var trxPath = Path.Combine(workspace, "xunit_results.trx");
            Console.WriteLine($"\n[harness] running xUnit tests against workspace");
            var testRc = RunDotnetTest(testsProj, workspace, trxPath);
            Console.WriteLine($"[harness] xUnit exit code: {testRc}");

            // 3) Build consolidated bench_result.json + archival copy
            var meta = new ReportBuilder.RunMeta(
                RunId: runId,
                StartedUtc: startedUtc,
                CompletedUtc: DateTime.UtcNow,
                Task: task,
                AgentSource: useOracle ? "oracle"
                          : useLocal  ? "in-repo-local"
                          : !string.IsNullOrEmpty(agentImage) ? "agent-image"
                          : !string.IsNullOrEmpty(submission) ? "submission"
                          : "in-repo-docker",
                AgentName: useOracle ? "AmlAgent.Oracle"
                          : !string.IsNullOrEmpty(submission) ? Path.GetFileName(Path.GetFullPath(submission))
                          : !string.IsNullOrEmpty(agentImage) ? agentImage
                          : agent,
                Model: model,
                MaxSteps: maxSteps,
                Mode: useOracle ? "oracle" : (useLocal ? "local" : "docker"));
            var outcomes = new ReportBuilder.HarnessOutcomes(
                AgentExitCode: agentRc,
                XUnitExitCode: testRc,
                JudgeExitCode: judgeRc,
                JudgeWasRun: !skipJudge && File.Exists(rubricPath));
            var report = ReportBuilder.Build(workspace, repoRoot, meta, outcomes, trxPath);
            PrintSummaryTables(report);

            JsonObject? assuranceProfile = null;
            bool invalidPolicyOrConfig = false;
            try
            {
                assuranceProfile = AssuranceProfileBuilder.Build(report, workspace, repoRoot, policyPath);
                if (assuranceProfile is not null)
                {
                    AssuranceProfileBuilder.Write(assuranceProfile, workspace, repoRoot, task, meta.AgentName, startedUtc);
                    PrintAssuranceProfileTables(assuranceProfile);
                }
            }
            catch (Exception ex)
            {
                // A bad policy (malformed JSON, unknown direction, impossible
                // threshold) or a profile that fails schema validation must
                // not silently produce a decision, but it also must not take
                // down an otherwise-successful benchmark run -- the
                // agent/judge/xUnit results above are still valid and
                // already written.
                Console.Error.WriteLine($"[harness] assurance profile rejected: {ex.Message}");
                invalidPolicyOrConfig = true;
            }

            var overall = ComputeExitCode(agentRc, testRc, judgeRc, invalidPolicyOrConfig, assuranceProfile);
            PrintExitCodeExplanation(overall);
            return overall;
        }
        finally
        {
            if (keepWorkspace) Console.WriteLine($"[harness] workspace kept: {workspace}");
            else SafeDelete(workspace);
        }
    }

    /// <summary>
    /// Meaningful, documented exit codes (CLI-Only Assurance Roadmap item 9)
    /// so the harness is usable as a CI/CD assurance gate:
    ///   0 = completed, benchmark passed, assurance PASS (or no assurance profile to evaluate)
    ///   1 = execution failure (the agent process itself failed)
    ///   2 = benchmark failure (xUnit and/or judge failed)
    ///   3 = benchmark passed, assurance PASS_WITH_CONDITIONS
    ///   4 = benchmark passed, assurance NOT_READY_FOR_DEPLOYMENT
    ///   5 = invalid policy/configuration (assurance profile could not be built/validated)
    /// Checked in this priority order: a malformed policy (5) is reported
    /// regardless of benchmark outcome since it means the requested
    /// assurance evaluation itself couldn't run; execution failure (1) and
    /// benchmark failure (2) take priority over the assurance decision
    /// because there's no valid benchmark result to base an assurance
    /// decision on in those cases.
    /// </summary>
    private static int ComputeExitCode(int agentRc, int xunitRc, int judgeRc, bool invalidPolicyOrConfig, JsonObject? assuranceProfile)
    {
        if (invalidPolicyOrConfig) return 5;
        if (agentRc != 0) return 1;
        if (xunitRc != 0 || judgeRc != 0) return 2;

        var decision = (string?)assuranceProfile?["status_summary"]?["assurance_decision"];
        return decision switch
        {
            "PASS_WITH_CONDITIONS" => 3,
            "NOT_READY_FOR_DEPLOYMENT" => 4,
            _ => 0, // PASS, or no assurance profile applicable (e.g. --oracle / --no-judge runs)
        };
    }

    private static void PrintExitCodeExplanation(int code)
    {
        var meaning = code switch
        {
            0 => "completed, benchmark passed, assurance PASS (or no assurance profile applicable)",
            1 => "execution failure -- the agent process itself failed",
            2 => "benchmark failure -- xUnit and/or judge failed",
            3 => "benchmark passed, assurance PASS_WITH_CONDITIONS",
            4 => "benchmark passed, assurance NOT_READY_FOR_DEPLOYMENT",
            5 => "invalid policy/configuration -- assurance profile could not be built",
            _ => "unrecognised",
        };
        Console.WriteLine($"[harness] exit code {code}: {meaning}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("aml-harness — Docker-based benchmark runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  aml-harness [agent-source] [--task <id>] [options]     run a benchmark + assurance profile");
        Console.WriteLine("  aml-harness compare <profile.json> <profile.json>...   compare two or more assurance profiles");
        Console.WriteLine("  aml-harness regress --baseline <p.json> --candidate <p.json>   detect an assurance regression");
        Console.WriteLine("  aml-harness load-dataset --source-type <type> [options]        load a dataset via the adapter layer, write dataset_manifest.json");
        Console.WriteLine("  aml-harness load-case --case <case-definition.json>            load+merge a multi-source case, write case_manifest.json");
        Console.WriteLine();
        Console.WriteLine("Agent source (pick one):");
        Console.WriteLine("  --agent <name>           subfolder of agents/ in this repo (default: csharp-sk)");
        Console.WriteLine("  --agent-image <tag>      use a pre-built Docker image as the agent");
        Console.WriteLine("  --submission <path>      build the Dockerfile in a local folder (user upload)");
        Console.WriteLine();
        Console.WriteLine("Other options:");
        Console.WriteLine("  --task <id>              task dir under tasks/ (default: aml-transaction-network)");
        Console.WriteLine("                           accepts a unique prefix, e.g. --task task-006 or --task 006");
        Console.WriteLine("  --model <id>             override BENCH_MODEL for the agent container");
        Console.WriteLine("  --max-steps <n>          cap on agent turns (default: 25)");
        Console.WriteLine("  --policy <path>          assurance policy to evaluate against (default: assurance/policy.default.json)");
        Console.WriteLine("                           e.g. --policy assurance/policies/bank-strict.json");
        Console.WriteLine("  --keep-workspace         keep the temp workspace dir after exit");
        Console.WriteLine("  --oracle                 use AmlAgent.Oracle instead of running an agent container");
        Console.WriteLine("                           (only valid for task=aml-transaction-network)");
        Console.WriteLine("  --local                  run the in-repo agent directly via `dotnet run` instead of Docker");
        Console.WriteLine("                           (only valid with --agent <name>; cannot combine with --agent-image / --submission)");
        Console.WriteLine("  --no-judge               skip the LLM-as-judge rubric stage");
        Console.WriteLine();
        Console.WriteLine("Exit codes (run mode):");
        Console.WriteLine("  0  completed, benchmark passed, assurance PASS (or no assurance profile applicable)");
        Console.WriteLine("  1  execution failure -- the agent process itself failed");
        Console.WriteLine("  2  benchmark failure -- xUnit and/or judge failed");
        Console.WriteLine("  3  benchmark passed, assurance PASS_WITH_CONDITIONS");
        Console.WriteLine("  4  benchmark passed, assurance NOT_READY_FOR_DEPLOYMENT");
        Console.WriteLine("  5  invalid policy/configuration -- assurance profile could not be built");
        Console.WriteLine();
        Console.WriteLine("Exit codes (compare/regress): 0 = ok, 1 = regression detected (regress only), 6 = invalid comparison");
        Console.WriteLine("Exit codes (load-dataset): 0 = ok, 64 = usage error, 65 = unsupported source type / invalid configuration / source or normalisation error");
        Console.WriteLine("Exit codes (load-case): 0 = ok, 64 = usage/definition error, 66 = a source failed to load, 67 = evidence integrity failed");
    }

    /// <summary>
    /// Resolves a --task value to a tasks/&lt;dir&gt; path. Tries an exact match
    /// first; if that fails, tries a unique prefix match (case-insensitive)
    /// against "&lt;task&gt;" and "task-&lt;task&gt;", so shorthand like
    /// "--task task-006" or "--task 006" resolves to
    /// "task-006-temporal-network-anomaly-detection" as long as it's unique.
    /// Returns null if nothing matches or the match is ambiguous.
    /// </summary>
    private static string? ResolveTaskDir(string repoRoot, string task)
    {
        var tasksRoot = Path.Combine(repoRoot, "tasks");
        var exact = Path.Combine(tasksRoot, task);
        if (Directory.Exists(exact)) return exact;

        if (!Directory.Exists(tasksRoot)) return null;

        var prefixes = new[] { task, $"task-{task}" };
        var candidates = Directory.GetDirectories(tasksRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && prefixes.Any(p =>
                name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

        if (candidates.Count == 1) return Path.Combine(tasksRoot, candidates[0]!);
        if (candidates.Count > 1)
            Console.Error.WriteLine($"[harness] ambiguous task '{task}' matches: {string.Join(", ", candidates)} — use the full task id");
        return null;
    }

    private static string ResolveAgentImage(string repoRoot, string agent, string? agentImage, string? submission)
    {
        if (!string.IsNullOrEmpty(agentImage))
        {
            Console.WriteLine($"[harness] using pre-built agent image: {agentImage}");
            return agentImage;
        }
        if (!string.IsNullOrEmpty(submission))
        {
            var subDir = Path.GetFullPath(submission);
            if (!Directory.Exists(subDir))
                throw new InvalidOperationException($"submission path not found: {subDir}");
            if (!File.Exists(Path.Combine(subDir, "Dockerfile")))
                throw new InvalidOperationException($"no Dockerfile in submission: {subDir}");
            var tag = $"aml-bench-submission-{Path.GetFileName(subDir).ToLowerInvariant()}:latest";
            var rc = RunProcess("docker", new[] { "build", "-t", tag, subDir });
            if (rc != 0) throw new InvalidOperationException("submission image build failed");
            return tag;
        }
        var agentDir = Path.Combine(repoRoot, "agents", agent);
        if (!Directory.Exists(agentDir))
            throw new InvalidOperationException($"agent not found: {agentDir}");
        var dockerfilePath = Path.Combine(agentDir, "Dockerfile");
        if (!File.Exists(dockerfilePath))
            throw new InvalidOperationException($"no Dockerfile: {dockerfilePath}");
        var defaultTag = $"aml-bench-agent-{agent}:latest";
        // Build context is the REPO ROOT, not agentDir: agents/csharp-sk's
        // AmlAgent.csproj references ../../src/AmlAgent.Evidence/, which
        // must be visible to the build. The root .dockerignore keeps the
        // upload small; each Dockerfile COPYs only the paths it needs.
        var brc = RunProcess("docker", new[] { "build", "-t", defaultTag, "-f", dockerfilePath, repoRoot });
        if (brc != 0) throw new InvalidOperationException("agent image build failed");
        return defaultTag;
    }

    private static void StageWorkspace(string taskDir, string workspace)
    {
        var envSrc = Path.Combine(taskDir, "environment");
        if (Directory.Exists(envSrc))
        {
            foreach (var entry in Directory.GetFileSystemEntries(envSrc))
            {
                var name = Path.GetFileName(entry);
                var dest = Path.Combine(workspace, name);
                if (Directory.Exists(entry)) CopyDir(entry, dest);
                else File.Copy(entry, dest, overwrite: true);
            }
        }
        // Stage all .md task files (instruction.md, prompt.md, expected-behaviour.md, tests.md)
        foreach (var name in new[] { "instruction.md", "prompt.md", "expected-behaviour.md", "tests.md" })
        {
            var src = Path.Combine(taskDir, name);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(workspace, name), overwrite: true);
        }
    }

    /// <summary>
    /// Opt-in multi-source case support: if the staged workspace contains a
    /// case-definition.json (a task's environment/ can now include one alongside,
    /// or instead of, a flat data/ file), resolve every source's adapter, load and
    /// merge them via CanonicalCaseMerger, validate cross-source evidence
    /// references, and materialise the result back into workspace/data/*.csv|json
    /// (CanonicalCaseExporter) plus workspace/case_manifest.json. Existing tasks
    /// have no case-definition.json, so this is a no-op for every task that
    /// predates this feature -- zero change to the run flow below.
    /// </summary>
    private static void StageCanonicalCaseIfPresent(string workspace)
    {
        var caseDefPath = Path.Combine(workspace, "case-definition.json");
        if (!File.Exists(caseDefPath)) return;

        Console.WriteLine("[harness] case-definition.json found -- loading multi-source canonical case");
        try
        {
            var definition = CaseDefinitionReader.Parse(File.ReadAllText(caseDefPath), caseDefPath, workspace);
            var result = CaseLoader.LoadAsync(definition, AdapterRegistry.CreateDefault()).GetAwaiter().GetResult();

            foreach (var entry in result.MergedCase.SourceManifest)
                Console.WriteLine($"[harness]   loaded {entry.SourceType,-12} adapter={entry.Adapter} v{entry.AdapterVersion} records={entry.RecordCount}");
            foreach (var failure in result.Failures)
                Console.Error.WriteLine($"[harness]   FAILED {failure.SourceType,-12} role={failure.Role ?? "?"}: {failure.ErrorMessage}");
            if (result.MergedCase.Conflicts.Count > 0)
                Console.WriteLine($"[harness]   {result.MergedCase.Conflicts.Count} merge conflict(s) -- see case_manifest.json");
            Console.WriteLine($"[harness]   evidence_integrity = {result.EvidenceIntegrity.Status}");

            var manifest = CaseManifestBuilder.Build(result, DateTimeOffset.UtcNow);
            CaseManifestBuilder.Write(manifest, Path.Combine(workspace, "case_manifest.json"));
            CanonicalCaseExporter.ExportToDirectory(result.MergedCase, Path.Combine(workspace, "data"));

            if (!result.AllSourcesLoaded || !result.EvidenceIntegrity.Passed)
                Console.Error.WriteLine("[harness]   WARNING: case has load failures or evidence-integrity failures -- see case_manifest.json (run continues; assurance evaluation gates on this separately)");
        }
        catch (InvalidCaseDefinitionException ex)
        {
            // A malformed case-definition.json is a task-authoring error, not a
            // reason to silently skip case loading -- surfaced loudly, but the
            // benchmark run itself still proceeds against whatever the task's
            // flat data/ files (if any) already provide.
            Console.Error.WriteLine($"[harness]   invalid case-definition.json: {ex.Message}");
        }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static int RunAgentContainer(string image, string workspace, string apiKey, string? model, int maxSteps)
    {
        var dockerArgs = new List<string>
        {
            "run", "--rm",
            "-v", $"{workspace}:/app",
            "-e", $"OPENAI_API_KEY={apiKey}",
            "-e", $"BENCH_MAX_STEPS={maxSteps}",
            "-e", "BENCH_TASK_DIR=/app",
        };
        if (!string.IsNullOrEmpty(model))
        {
            dockerArgs.Add("-e");
            dockerArgs.Add($"BENCH_MODEL={model}");
        }
        dockerArgs.Add(image);
        return RunProcess("docker", dockerArgs);
    }

    private static int RunAgentLocal(string repoRoot, string agent, string workspace, string? model, int maxSteps)
    {
        // Currently --local is only wired for csharp-sk because it's the
        // only in-repo agent that ships as a .csproj. Other agents could be
        // added with a name → invocation map.
        if (agent != "csharp-sk")
        {
            Console.Error.WriteLine($"[harness] --local is only supported for --agent csharp-sk (got {agent})");
            return 1;
        }
        var agentProj = Path.Combine(repoRoot, "agents", "csharp-sk", "AmlAgent.csproj");
        var env = new Dictionary<string, string>
        {
            ["BENCH_TASK_DIR"] = workspace,
            ["BENCH_MAX_STEPS"] = maxSteps.ToString(),
        };
        if (!string.IsNullOrEmpty(model)) env["BENCH_MODEL"] = model;
        return RunProcess("dotnet", new[]
        {
            "run", "--project", agentProj, "--no-build", "--", "run"
        }, env);
    }

    private static int RunDotnetTest(string testsProj, string workspace, string trxPath)
    {
        var env = new Dictionary<string, string> { ["AML_BENCH_WORKSPACE"] = workspace };
        return RunProcess("dotnet", new[]
        {
            "test", testsProj, "--nologo", "-v", "minimal",
            "--logger", $"trx;LogFileName={trxPath}",
        }, env);
    }

    private static int RunJudge(string repoRoot, string task, string workspace)
    {
        var agentProj = Path.Combine(repoRoot, "agents", "csharp-sk", "AmlAgent.csproj");
        return RunProcess("dotnet", new[]
        {
            "run", "--project", agentProj, "--no-build", "--",
            "judge", "--task", task, "--workspace", workspace
        });
    }

    private static int RunProcess(string file, IEnumerable<string> args, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;

        Console.WriteLine($"$ {file} {string.Join(' ', args.Select(RedactForLog))}");
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>
    /// Redacts the value of any "NAME=value" argument whose name looks like
    /// a secret (KEY/TOKEN/SECRET/PASSWORD), so echoed commands — e.g.
    /// `docker run -e OPENAI_API_KEY=...` — never print credentials to the
    /// console or any captured log/transcript.
    /// </summary>
    private static string RedactForLog(string arg)
    {
        var eq = arg.IndexOf('=');
        if (eq <= 0) return arg;
        var name = arg[..eq];
        var upper = name.ToUpperInvariant();
        if (upper.Contains("KEY") || upper.Contains("TOKEN") || upper.Contains("SECRET") || upper.Contains("PASSWORD"))
            return $"{name}=***REDACTED***";
        return arg;
    }

    /// <summary>
    /// Prints a clean, aligned recap of the run — judge scoring (including
    /// EGHR and evidence traceability), xUnit results, and the overall
    /// verdict — as plain-ASCII tables. Read straight off the same
    /// bench_result.json ReportBuilder just wrote, so it never drifts from
    /// what's archived to results/. Kept separate from the live per-step
    /// agent trace (which streams from the agent's own process) so a demo
    /// still shows the agent working in real time before this final recap.
    /// </summary>
    private static void PrintSummaryTables(JsonObject report)
    {
        Console.WriteLine();
        Console.WriteLine("==================== RUN SUMMARY ====================");

        var agentObj = report["agent"]?.AsObject();
        Console.WriteLine($"Task  : {(string?)report["task"]}");
        Console.WriteLine($"Agent : {(string?)agentObj?["name"]}  ({(string?)agentObj?["mode"]})");
        Console.WriteLine($"Model : {(string?)agentObj?["model"] ?? "(default)"}");

        PrintSectionBanner("AGENT OUTPUT — what the agent actually produced, before any judging");
        PrintAgentOutputSection(report);

        var judge = report["judge"]?.AsObject();
        if (judge is not null && judge["scores"] is not null)
        {
            PrintSectionBanner("JUDGE EVALUATION — the LLM-as-judge's assessment of the output above");
            PrintJudgeOverview(judge);
            PrintRubricDimensionTables(judge);
            PrintEghrTables(judge);
            PrintTraceabilityTables(judge);
        }
        else
        {
            PrintSectionBanner("JUDGE EVALUATION");
            Console.WriteLine("-- not run for this task --");
        }

        var xunit = report["xunit"]?.AsObject();
        if (xunit is not null)
        {
            PrintSectionBanner("STRUCTURAL TESTS (xUnit) — deterministic checks, independent of the judge");
            PrintTable("xUnit", new[] { "Outcome", "Count" }, new List<string[]>
            {
                new[] { "Passed", $"{(int?)xunit["passed"] ?? 0}" },
                new[] { "Skipped", $"{(int?)xunit["skipped"] ?? 0}" },
                new[] { "Failed", $"{(int?)xunit["failed"] ?? 0}" },
                new[] { "Total", $"{(int?)xunit["total"] ?? 0}" },
            });

            var failures = xunit["failures"]?.AsArray();
            if (failures is not null && failures.Count > 0)
            {
                var failRows = failures
                    .Select(f => new[] { (string?)f?["test_name"] ?? "?", (string?)f?["message"] ?? "" })
                    .ToList();
                PrintTable("xUnit failures", new[] { "Test", "Message" }, failRows);
            }
        }

        var overallVerdict = (string?)report["overall_verdict"] ?? "-";
        var judgeExitDisplay = judge is null ? "n/a (no rubric)"
            : (bool?)judge["was_run"] == false ? "n/a (skipped)"
            : (string?)judge["verdict"] == "PASS" ? "0 (PASS)"
            : "1 (FAIL)";
        var xunitExitDisplay = xunit is null ? "n/a" : $"{(int?)xunit["exit_code"] ?? 0} ({(string?)xunit["verdict"]})";

        PrintSectionBanner("OVERALL");
        PrintTable("Overall", new[] { "Field", "Value" }, new List<string[]>
        {
            new[] { "Agent exit", $"{(int?)report["agent_exit_code"] ?? 0}" },
            new[] { "xUnit exit", xunitExitDisplay },
            new[] { "Judge exit", judgeExitDisplay },
            new[] { "OVERALL", overallVerdict },
            new[] { "Reason", (string?)report["overall_reason"] ?? "-" },
        });
        Console.WriteLine("=======================================================");
    }

    private static void PrintSectionBanner(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"---------------------- {title} ----------------------");
    }

    /// <summary>
    /// Prints exactly what the agent produced — the raw CSV rows and the raw
    /// report text — before any judge/xUnit result, so a reader can see the
    /// agent's own work first and then compare it against how it was scored.
    /// Reads report["agent_outputs"], which ReportBuilder.CollectAgentOutputs
    /// already populated from the workspace files.
    /// </summary>
    private static void PrintAgentOutputSection(JsonObject report)
    {
        var outputs = report["agent_outputs"]?.AsObject();
        if (outputs is null || outputs.Count == 0)
        {
            Console.WriteLine("-- no output files found --");
            return;
        }

        foreach (var (name, node) in outputs)
        {
            var obj = node?.AsObject();
            if (obj is null) continue;
            var size = (long?)obj["size_bytes"] ?? 0;

            if (obj["rows"] is JsonArray rows && rows.Count > 0)
            {
                var headers = rows[0]!.AsObject().Select(kv => kv.Key).ToArray();
                var dataRows = rows
                    .Select(r => headers.Select(h => r?[h]?.ToString() ?? "").ToArray())
                    .ToList();
                PrintTable($"{name}  ({size} bytes)", headers, dataRows);
            }
            else if (obj["content_preview"] is JsonValue previewNode)
            {
                var text = previewNode.GetValue<string>();
                var citationCount = (int?)obj["citation_count"];
                var citationNote = citationCount is int c ? $", {c} txn-ID citations found by regex" : "";
                var title = $"{name}  ({size} bytes{citationNote})";

                if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var sectionRows = ParseMarkdownSections(text);
                    if (sectionRows.Count > 0)
                        PrintTable(title, new[] { "Section", "Content" }, sectionRows);
                    else
                        PrintRawText(title, text);
                }
                else
                {
                    PrintRawText(title, text);
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"-- {name}  ({size} bytes) — binary or unrecognised format, not previewed --");
            }
        }
    }

    private static void PrintRawText(string title, string text)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {title} --");
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            Console.WriteLine($"   {line}");
    }

    /// <summary>
    /// Breaks a markdown report into (Section, Content) rows for tabular
    /// display: each "#"/"##" heading starts a new section; a bullet list
    /// item under a heading becomes its own row (so e.g. "Week-by-week
    /// Analysis" reads as one row per week instead of one wall of text);
    /// any other non-blank text is grouped into a single paragraph row.
    /// </summary>
    private static List<string[]> ParseMarkdownSections(string markdown)
    {
        var rows = new List<string[]>();
        var section = "(intro)";
        var buffer = new List<string>();

        void Flush()
        {
            var text = string.Join(' ', buffer).Trim();
            if (text.Length > 0)
                rows.Add(new[] { section, Truncate(text, 90) });
            buffer.Clear();
        }

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('#'))
            {
                Flush();
                section = line.TrimStart('#', ' ').Trim();
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                Flush(); // any preceding paragraph under this heading becomes its own row first
                rows.Add(new[] { section, Truncate(line[2..].Trim(), 90) });
            }
            else
            {
                buffer.Add(line);
            }
        }
        Flush();
        return rows;
    }

    /// <summary>Quick-scan headline numbers — the five figures people ask about first.</summary>
    private static void PrintJudgeOverview(JsonObject judge)
    {
        var rows = new List<string[]>
        {
            new[] { "Rubric score", $"{(int?)judge["overall_score"]}/{(int?)judge["overall_max"]} ({(double?)judge["overall_percentage"]:P1})" },
            new[] { "Rubric verdict", (string?)judge["verdict"] ?? "-" },
        };

        var eghr = judge["eghr"]?.AsObject();
        if (eghr is not null)
            rows.Add(new[] { "EGHR", $"{(double?)eghr["rate"]:P1} ({(int?)eghr["unsupported_count"]} unsupported + {(int?)eghr["contradicted_count"]} contradicted / {(int?)eghr["total_claims"]} claims)" });

        var trace = judge["evidence_traceability"]?.AsObject();
        if (trace is not null)
        {
            rows.Add(new[] { "Evidence traceability precision", FormatPercentOrNa(trace["precision"]) });
            var goldTotal = (int?)trace["gold_evidence_total"] ?? 0;
            var matched = (int?)trace["matched_gold_citations"] ?? 0;
            rows.Add(new[] { "Evidence traceability recall", $"{FormatPercentOrNa(trace["recall"])} ({matched}/{goldTotal} gold citations)" });
        }

        PrintTable("Judge scoring — overview", new[] { "Metric", "Value" }, rows);
        Console.WriteLine("   (full breakdown of every number below)");
    }

    /// <summary>
    /// Drill-down for "Rubric score" / "Rubric verdict": every one of the six
    /// rubric dimensions that summed to the overall score, plus the pass/fail
    /// arithmetic itself.
    /// </summary>
    private static void PrintRubricDimensionTables(JsonObject judge)
    {
        var scores = judge["scores"]?.AsObject();
        if (scores is not null)
        {
            var rows = scores.Select(kv => new[]
            {
                kv.Key,
                $"{(int?)kv.Value?["score"]}/{(int?)kv.Value?["max"]}",
                (string?)kv.Value?["reasoning"] ?? "",
            }).ToList();
            PrintTable("Rubric — per-dimension scores (sums to the overall score above)", new[] { "Dimension", "Score", "Reasoning" }, rows);
        }

        var overallScore = (int?)judge["overall_score"] ?? 0;
        var overallMax = (int?)judge["overall_max"] ?? 0;
        var percentage = (double?)judge["overall_percentage"];
        var threshold = (double?)judge["pass_threshold_overall"];
        var verdict = (string?)judge["verdict"] ?? "-";

        var comparison = percentage is double p && threshold is double t
            ? $"{p:P1} {(p >= t ? ">=" : "<")} {t:P1} threshold  =>  {(p >= t ? "PASS" : "FAIL")}"
            : "n/a";

        PrintTable("Rubric — verdict arithmetic (why the verdict is what it is)", new[] { "Field", "Value" }, new List<string[]>
        {
            new[] { "Overall score", $"{overallScore}  (sum of the six dimension scores above)" },
            new[] { "Overall max", $"{overallMax}  (sum of the six dimension maxes)" },
            new[] { "Overall percentage", $"{FormatPercentOrNa(judge["overall_percentage"])}  ({overallScore}/{overallMax})" },
            new[] { "Pass threshold", FormatPercentOrNa(judge["pass_threshold_overall"]) },
            new[] { "Rule", "verdict = PASS if overall percentage >= pass threshold, else FAIL" },
            new[] { "Comparison", comparison },
            new[] { "Verdict", verdict },
        });
    }

    /// <summary>
    /// Drill-down for "EGHR": every atomic claim the judge extracted, its
    /// citations, and its support label (with fabricated citations flagged —
    /// those are forced to "unsupported" deterministically regardless of
    /// what the LLM said, see AmlAgent.Evidence.EvidenceScoring.ScoreClaims).
    /// </summary>
    private static void PrintEghrTables(JsonObject judge)
    {
        var eghr = judge["eghr"]?.AsObject();
        if (eghr is null) return;

        var claims = judge["claims"]?.AsArray();
        if (claims is not null && claims.Count > 0)
        {
            var rows = claims.Select((c, i) => new[]
            {
                $"{i + 1}",
                Truncate((string?)c?["text"] ?? "", 70),
                string.Join(", ", c?["cited_txn_ids"]?.AsArray()?.Select(n => (string?)n) ?? Array.Empty<string?>()),
                (string?)c?["support"] ?? "-",
                (bool?)c?["fabricated_citation"] == true ? "YES" : "",
            }).ToList();
            PrintTable("EGHR — claims (each one scored supported / unsupported / contradicted)",
                new[] { "#", "Claim", "Cited txn IDs", "Support", "Fabricated?" }, rows);
        }

        PrintTable("EGHR — summary (why the rate is what it is)", new[] { "Field", "Value" }, new List<string[]>
        {
            new[] { "Total claims", $"{(int?)eghr["total_claims"]}" },
            new[] { "Supported", $"{(int?)eghr["supported_count"]}" },
            new[] { "Unsupported (extrinsic)", $"{(int?)eghr["unsupported_count"]}" },
            new[] { "Contradicted (intrinsic)", $"{(int?)eghr["contradicted_count"]}" },
            new[] { "Rate = (unsupported + contradicted) / total", FormatPercentOrNa(eghr["rate"]) },
        });
    }

    /// <summary>
    /// Drill-down for "Evidence traceability precision/recall": exactly
    /// which transaction IDs the report cited, which of those were fabricated
    /// (don't exist in the source data), and which of the curated gold-evidence
    /// transactions were actually covered vs. missed.
    /// </summary>
    private static void PrintTraceabilityTables(JsonObject judge)
    {
        var trace = judge["evidence_traceability"]?.AsObject();
        if (trace is null) return;

        var grounded = StringsOf(trace["grounded_citations"]);
        var fabricated = StringsOf(trace["fabricated_citations"]);
        var citationRows = grounded.Select(id => new[] { id, "real (in source data)" })
            .Concat(fabricated.Select(id => new[] { id, "FABRICATED — not in source data" }))
            .ToList();
        if (citationRows.Count > 0)
            PrintTable("Evidence traceability — every cited transaction (why precision is what it is)",
                new[] { "Cited txn ID", "Status" }, citationRows);
        else
            Console.WriteLine("\n-- Evidence traceability — report cited no transaction IDs at all --");

        var gold = StringsOf(trace["gold_evidence_txn_ids"]);
        var matched = new HashSet<string>(StringsOf(trace["matched_gold_citations_list"]), StringComparer.OrdinalIgnoreCase);
        if (gold.Count > 0)
        {
            var goldRows = gold.Select(id => new[] { id, matched.Contains(id) ? "cited" : "MISSING — not cited" }).ToList();
            PrintTable("Evidence traceability — gold-evidence coverage (why recall is what it is)",
                new[] { "Gold-evidence txn ID", "Covered?" }, goldRows);
        }

        PrintTable("Evidence traceability — summary", new[] { "Field", "Value" }, new List<string[]>
        {
            new[] { "Cited txn IDs (total mentions)", $"{(int?)trace["cited_txn_ids_total"] ?? 0}" },
            new[] { "Cited txn IDs (distinct)", $"{(int?)trace["cited_txn_ids_distinct"] ?? 0}" },
            new[] { "Grounded (real) citations", $"{(int?)trace["grounded_citations_distinct"] ?? 0}" },
            new[] { "Fabricated citations", $"{fabricated.Count}" },
            new[] { "Gold-evidence set size", $"{(int?)trace["gold_evidence_total"] ?? 0}" },
            new[] { "Matched gold citations", $"{(int?)trace["matched_gold_citations"] ?? 0}" },
            new[] { "Precision = matched / grounded citations", FormatPercentOrNa(trace["precision"]) },
            new[] { "Recall = matched / gold-evidence size", FormatPercentOrNa(trace["recall"]) },
            new[] { "F1", FormatPercentOrNa(trace["f1"]) },
        });
    }

    /// <summary>
    /// Prints the assurance-profile decision as its own clearly separated
    /// section: which of the policy's metrics passed/failed/were not
    /// evaluated, which whole dimensions this benchmark doesn't measure yet
    /// (never silently hidden), and the resulting deployment decision. See
    /// assurance/README.md for what this is and, more importantly, what it
    /// deliberately does not claim.
    /// </summary>
    private static void PrintAssuranceProfileTables(JsonObject profile)
    {
        PrintSectionBanner("ASSURANCE PROFILE (prototype) — see assurance/README.md before citing this anywhere");

        // Three separate concepts, printed side by side on purpose: a
        // benchmark PASS (xUnit + judge) must never be read as a deployment
        // PASS. They're allowed to disagree, and often do.
        var status = profile["status_summary"]?.AsObject();
        PrintTable("Status (execution vs. benchmark vs. assurance are separate)", new[] { "Field", "Value" }, new List<string[]>
        {
            new[] { "Execution status", (string?)status?["execution_status"] ?? "-" },
            new[] { "Benchmark verdict (xUnit + judge)", (string?)status?["benchmark_verdict"] ?? "-" },
            new[] { "Assurance decision (this policy)", (string?)status?["assurance_decision"] ?? "-" },
        });

        var policy = profile["policy"]?.AsObject();
        Console.WriteLine($"Policy: {(string?)policy?["id"]} v{(string?)policy?["version"]} — {(string?)policy?["name"]}  (illustrative example, not a real institution's policy — {(string?)policy?["path"]})");

        var capabilities = StringsOf(profile["operational_capabilities"]);
        Console.WriteLine(capabilities.Count > 0
            ? $"Operational capabilities tested: {string.Join(", ", capabilities)}"
            : "Operational capabilities tested: (not tagged for this task — see tasks/<id>/capabilities.json)");

        var metrics = profile["metrics"]?.AsArray();
        if (metrics is not null && metrics.Count > 0)
        {
            var rows = metrics.Select(m => new[]
            {
                (string?)m?["label"] ?? "",
                FormatMetricValue(m?["value"], (string?)m?["unit"]),
                FormatMetricValue(m?["threshold"], (string?)m?["unit"]),
                (bool?)m?["required"] == false ? "optional" : "required",
                (string?)m?["status"] ?? "-",
            }).ToList();
            PrintTable("Policy metrics", new[] { "Metric", "Measured", "Threshold", "Tier", "Status" }, rows);
        }

        var notEvaluated = StringsOf(profile["not_evaluated_dimensions"]);
        if (notEvaluated.Count > 0)
        {
            PrintTable("Dimensions NOT evaluated by this benchmark (honestly disclosed, not hidden)",
                new[] { "Dimension" }, notEvaluated.Select(d => new[] { d }).ToList());
        }

        var decision = profile["deployment_decision"]?.AsObject();
        PrintTable("Deployment decision", new[] { "Field", "Value" }, new List<string[]>
        {
            new[] { "Overall", (string?)decision?["overall"] ?? "-" },
            new[] { "Reason", (string?)decision?["reason"] ?? "-" },
            new[] { "Metrics evaluated", $"{(int?)decision?["evaluated_metric_count"] ?? 0} of {(int?)decision?["total_defined_dimension_count"] ?? 0} defined dimensions" },
        });

        var reasons = decision?["reasons"]?.AsArray();
        if (reasons is not null && reasons.Count > 0)
        {
            var rows = reasons.Select(r => new[]
            {
                (string?)r?["label"] ?? "",
                FormatMetricValue(r?["actual"], "auto"),
                (string?)r?["rule"] ?? "",
                FormatMetricValue(r?["threshold"], "auto"),
                (string?)r?["severity"] ?? "",
            }).ToList();
            PrintTable("Structured decision reasons", new[] { "Metric", "Actual", "Rule", "Threshold", "Severity" }, rows);
        }

        var restrictions = profile["deployment_restrictions"]?.AsObject();
        if (restrictions is not null)
        {
            var rows = new List<string[]>();
            foreach (var item in StringsOf(restrictions["permitted"])) rows.Add(new[] { "Permitted", item });
            foreach (var item in StringsOf(restrictions["human_approval_required"])) rows.Add(new[] { "Human approval required", item });
            foreach (var item in StringsOf(restrictions["not_permitted"])) rows.Add(new[] { "Not permitted", item });
            if (rows.Count > 0)
                PrintTable("Illustrative deployment restrictions (if PASS_WITH_CONDITIONS)", new[] { "Category", "Use" }, rows);
        }

        var provenance = profile["provenance"]?.AsObject();
        PrintTable("Provenance (for reproducing this exact decision)", new[] { "Field", "Value" }, new List<string[]>
        {
            new[] { "Benchmark version", (string?)provenance?["benchmark_version"] ?? "-" },
            new[] { "Git commit SHA", (string?)provenance?["git_commit_sha"] ?? "n/a (git not available)" },
            new[] { "Policy", $"{(string?)provenance?["policy_id"]} v{(string?)provenance?["policy_version"]}" },
            new[] { "Execution mode", (string?)provenance?["execution_mode"] ?? "-" },
            new[] { "Dataset hash", (string?)provenance?["dataset_hash"] ?? "n/a" },
            new[] { "Rubric hash", (string?)provenance?["rubric_hash"] ?? "n/a" },
            new[] { "Run ID", (string?)provenance?["run_id"] ?? "-" },
        });

        Console.WriteLine($"Result hash: {(string?)profile["result_hash"]}");
    }

    private static string FormatMetricValue(JsonNode? node, string? unit)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null) return "n/a";
        var d = node.GetValue<double>();
        return unit == "rate" ? $"{d:P1}" : d.ToString("0.####");
    }

    private static List<string> StringsOf(JsonNode? arrayNode) =>
        arrayNode?.AsArray()?.Select(n => (string?)n ?? "").ToList() ?? new List<string>();

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    private static string FormatPercentOrNa(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null) return "n/a";
        return $"{node.GetValue<double>():P1}";
    }

    internal static void PrintTable(string title, string[] headers, IReadOnlyList<string[]> rows)
    {
        var widths = headers
            .Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length)))
            .ToArray();

        Console.WriteLine();
        Console.WriteLine($"-- {title} --");
        PrintTableRow(headers, widths);
        Console.WriteLine(string.Join("-+-", widths.Select(w => new string('-', w))));
        foreach (var r in rows) PrintTableRow(r, widths);
    }

    private static void PrintTableRow(string[] cells, int[] widths) =>
        Console.WriteLine(string.Join(" | ", cells.Select((c, i) => c.PadRight(widths[i]))));

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AML-Agent-Bench.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static void SafeDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
