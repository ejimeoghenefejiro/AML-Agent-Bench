using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AmlAgent.Harness;

/// <summary>
/// `aml-harness experiment repeat --task &lt;id&gt; [--agent &lt;name&gt;] [--runs N]
///   [--model &lt;id&gt;] [--policy &lt;path&gt;] [--out &lt;path&gt;]`
///
/// Research-validation item 6: runs the SAME task + agent + model + configuration
/// N times (each a genuine, independent nested `aml-harness --local` invocation --
/// not a mock or a cached replay) and captures RAW per-run measurements: benchmark
/// verdict, assurance decision, rubric score, EGHR, traceability precision/recall/
/// F1, fabricated citation count, cited evidence ids, structured findings (if the
/// task produces a *findings*.csv), and latency. Deliberately does NOT compute or
/// invent a single "consistency score" across runs -- per the instructions, that
/// scientific definition hasn't been settled yet, so this only captures the raw
/// data a later methodological pass would need.
/// </summary>
internal static class ExperimentRepeatCommand
{
    private static readonly Regex WorkspaceLine = new(@"\[harness\]\s+workspace\s*=\s*(?<path>.+)$", RegexOptions.Multiline);

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        string task = "aml-transaction-network";
        string agent = "csharp-sk";
        int runs = 5;
        string? model = null;
        string? policyPath = null;
        string outPath = "repeated_run_result.json";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--task"   when i + 1 < args.Length: task = args[++i]; break;
                case "--agent"  when i + 1 < args.Length: agent = args[++i]; break;
                case "--runs"   when i + 1 < args.Length: runs = int.Parse(args[++i]); break;
                case "--model"  when i + 1 < args.Length: model = args[++i]; break;
                case "--policy" when i + 1 < args.Length: policyPath = args[++i]; break;
                case "--out"    when i + 1 < args.Length: outPath = args[++i]; break;
                default:
                    Console.Error.WriteLine($"experiment repeat: unknown argument: {args[i]}");
                    PrintUsage();
                    return 64;
            }
        }

        if (runs < 1)
        {
            Console.Error.WriteLine("experiment repeat: --runs must be >= 1");
            return 64;
        }

        var repoRoot = Program.FindRepoRoot()
            ?? throw new InvalidOperationException("Could not locate repo root (looking for AML-Agent-Bench.sln)");
        var harnessProj = Path.Combine(repoRoot, "src", "AmlAgent.Harness", "AmlAgent.Harness.csproj");

        var records = new List<JsonObject>();
        for (int run = 1; run <= runs; run++)
        {
            Console.WriteLine($"[experiment repeat] run {run}/{runs} -- task={task} agent={agent} model={model ?? "(default)"}");
            var record = RunOnce(harnessProj, task, agent, model, policyPath, run);
            records.Add(record);
            Console.WriteLine($"[experiment repeat]   verdict={(string?)record["benchmark_verdict"] ?? "?"} " +
                $"assurance={(string?)record["assurance_decision"] ?? "?"} " +
                $"eghr={record["eghr_rate"]?.ToString() ?? "?"} " +
                $"latency={(double?)record["latency_seconds"]:0.0}s");
        }

        var result = new JsonObject
        {
            ["experiment_type"] = "repeated_run",
            ["task"] = task,
            ["agent"] = agent,
            ["model"] = model,
            ["policy_path"] = policyPath,
            ["requested_runs"] = runs,
            ["generated_at_utc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["note"] = "Raw per-run measurements only -- no aggregated consistency metric is computed here (not yet formally defined; see research-validation instructions item 6).",
            ["runs"] = new JsonArray(records.Select(r => (JsonNode)r).ToArray()),
        };

        File.WriteAllText(outPath, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[experiment repeat] wrote {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static JsonObject RunOnce(string harnessProj, string task, string agent, string? model, string? policyPath, int runIndex)
    {
        var runArgs = new List<string> { "run", "--project", harnessProj, "--no-build", "--", "--task", task, "--agent", agent, "--local", "--keep-workspace" };
        if (!string.IsNullOrEmpty(model)) { runArgs.Add("--model"); runArgs.Add(model); }
        if (!string.IsNullOrEmpty(policyPath)) { runArgs.Add("--policy"); runArgs.Add(policyPath); }

        var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in runArgs) psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        sw.Stop();

        var record = new JsonObject { ["run_index"] = runIndex, ["exit_code"] = p.ExitCode, ["latency_seconds"] = Math.Round(sw.Elapsed.TotalSeconds, 2) };

        var match = WorkspaceLine.Match(stdout);
        if (!match.Success)
        {
            record["error"] = "could not locate workspace path in nested run output";
            record["stderr_tail"] = Program.RedactForLog(Tail(stderr, 2000));
            return record;
        }

        var workspace = match.Groups["path"].Value.Trim();
        record["workspace"] = workspace;

        try
        {
            PopulateFromWorkspace(record, workspace);
        }
        catch (Exception ex)
        {
            record["parse_error"] = ex.Message;
        }

        return record;
    }

    private static void PopulateFromWorkspace(JsonObject record, string workspace)
    {
        var benchResultPath = Path.Combine(workspace, "bench_result.json");
        if (File.Exists(benchResultPath))
        {
            var benchResult = JsonNode.Parse(File.ReadAllText(benchResultPath))?.AsObject();
            record["benchmark_verdict"] = (string?)benchResult?["overall_verdict"];
        }

        var assurancePath = Path.Combine(workspace, "assurance_profile.json");
        if (File.Exists(assurancePath))
        {
            var profile = JsonNode.Parse(File.ReadAllText(assurancePath))?.AsObject();
            record["assurance_decision"] = (string?)profile?["status_summary"]?["assurance_decision"];
            var metrics = profile?["metrics"]?.AsArray();
            double? Find(string metric) => (double?)metrics?.FirstOrDefault(m => (string?)m?["metric"] == metric)?["value"];
            record["rubric_overall_percentage"] = Find("task_performance_percentage");
            record["eghr_rate"] = Find("eghr_rate");
            record["traceability_f1"] = Find("evidence_traceability_f1");
            record["fabricated_citation_count"] = Find("fabricated_citation_count");
            var trace = profile?["evidence_summary"]?["evidence_traceability"];
            record["traceability_precision"] = (double?)trace?["precision"];
            record["traceability_valid_evidence_precision"] = (double?)trace?["valid_evidence_precision"];
            record["traceability_recall"] = (double?)trace?["recall"];
            record["cited_evidence_ids"] = trace?["grounded_citations"]?.DeepClone();
        }

        var findingsFile = Directory.Exists(workspace)
            ? Directory.GetFiles(workspace, "*findings*.csv").FirstOrDefault()
            : null;
        if (findingsFile is not null)
            record["structured_findings"] = ParseCsvAsJsonArray(findingsFile);

        var manifestPath = Path.Combine(workspace, "case_manifest.json");
        if (File.Exists(manifestPath))
        {
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
            record["case_evidence_integrity_status"] = (string?)manifest?["evidence_integrity"]?["status"];
        }
    }

    private static JsonArray ParseCsvAsJsonArray(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) return new JsonArray();
        var header = lines[0].Split(',');
        var rows = new JsonArray();
        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');
            var row = new JsonObject();
            for (int c = 0; c < header.Length && c < cols.Length; c++)
                row[header[c]] = cols[c];
            rows.Add(row);
        }
        return rows;
    }

    private static string Tail(string s, int maxChars) => s.Length <= maxChars ? s : s[^maxChars..];

    private static void PrintUsage()
    {
        Console.WriteLine("aml-harness experiment repeat --task <id> [options]");
        Console.WriteLine();
        Console.WriteLine("Runs the same task+agent+model+configuration N times via nested --local runs,");
        Console.WriteLine("capturing raw per-run measurements (no aggregated consistency metric).");
        Console.WriteLine();
        Console.WriteLine("  --task <id>       task dir under tasks/ (default: aml-transaction-network)");
        Console.WriteLine("  --agent <name>    in-repo agent to run (default: csharp-sk)");
        Console.WriteLine("  --runs <n>        repetition count (default: 5)");
        Console.WriteLine("  --model <id>      override BENCH_MODEL for every run");
        Console.WriteLine("  --policy <path>   assurance policy to evaluate against");
        Console.WriteLine("  --out <path>      output path (default: repeated_run_result.json)");
        Console.WriteLine();
        Console.WriteLine("Requires OPENAI_API_KEY -- each run is a genuine agent + judge invocation.");
        Console.WriteLine("Exit codes: 0 = completed (see individual run records for per-run outcomes), 64 = usage error.");
    }
}
