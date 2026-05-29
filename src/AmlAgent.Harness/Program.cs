using System.Diagnostics;
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

        var taskDir = Path.Combine(repoRoot, "tasks", task);
        if (!Directory.Exists(taskDir)) { Console.Error.WriteLine($"task not found: {taskDir}"); return 1; }

        var runId = Guid.NewGuid().ToString("N");
        var startedUtc = DateTime.UtcNow;
        var workspace = Path.Combine(Path.GetTempPath(), $"aml-bench-{task}-{runId}");
        Directory.CreateDirectory(workspace);
        try
        {
            StageWorkspace(taskDir, workspace);
            Console.WriteLine($"[harness] task     = {task}");
            Console.WriteLine($"[harness] workspace = {workspace}");

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
            ReportBuilder.Build(workspace, repoRoot, meta, outcomes, trxPath);

            var overall = (testRc == 0 && judgeRc == 0) ? 0 : 1;
            Console.WriteLine($"\n[harness] OVERALL: {(overall == 0 ? "PASS" : "FAIL")} (xunit={testRc} judge={judgeRc})");
            return overall;
        }
        finally
        {
            if (keepWorkspace) Console.WriteLine($"[harness] workspace kept: {workspace}");
            else SafeDelete(workspace);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("aml-harness — Docker-based benchmark runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  aml-harness [agent-source] [--task <id>] [options]");
        Console.WriteLine();
        Console.WriteLine("Agent source (pick one):");
        Console.WriteLine("  --agent <name>           subfolder of agents/ in this repo (default: csharp-sk)");
        Console.WriteLine("  --agent-image <tag>      use a pre-built Docker image as the agent");
        Console.WriteLine("  --submission <path>      build the Dockerfile in a local folder (user upload)");
        Console.WriteLine();
        Console.WriteLine("Other options:");
        Console.WriteLine("  --task <id>              task dir under tasks/ (default: aml-transaction-network)");
        Console.WriteLine("  --model <id>             override BENCH_MODEL for the agent container");
        Console.WriteLine("  --max-steps <n>          cap on agent turns (default: 25)");
        Console.WriteLine("  --keep-workspace         keep the temp workspace dir after exit");
        Console.WriteLine("  --oracle                 use AmlAgent.Oracle instead of running an agent container");
        Console.WriteLine("                           (only valid for task=aml-transaction-network)");
        Console.WriteLine("  --local                  run the in-repo agent directly via `dotnet run` instead of Docker");
        Console.WriteLine("                           (only valid with --agent <name>; cannot combine with --agent-image / --submission)");
        Console.WriteLine("  --no-judge               skip the LLM-as-judge rubric stage");
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
        var defaultTag = $"aml-bench-agent-{agent}:latest";
        var brc = RunProcess("docker", new[] { "build", "-t", defaultTag, agentDir });
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

        Console.WriteLine($"$ {file} {string.Join(' ', args)}");
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

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
