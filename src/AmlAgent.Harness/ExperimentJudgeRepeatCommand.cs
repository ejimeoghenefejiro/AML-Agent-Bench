using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmlAgent.Harness;

/// <summary>
/// `aml-harness experiment judge-repeat --workspace &lt;path&gt; --task &lt;id&gt; [--runs N] [--out &lt;path&gt;]`
///
/// Research-validation item 7: takes a FIXED, already-produced agent output (an
/// existing workspace -- typically staged with --keep-workspace from a prior run)
/// and re-runs ONLY the LLM judge against it N times with the same configuration,
/// to measure how stable/unstable LLM-as-judge scoring actually is. Every
/// individual judge_report.json is captured in full, not just an average --
/// per the instructions, "store every individual result".
/// </summary>
internal static class ExperimentJudgeRepeatCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        string? workspace = null;
        string? task = null;
        int runs = 5;
        string outPath = "judge_repeatability_result.json";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--workspace" when i + 1 < args.Length: workspace = args[++i]; break;
                case "--task"      when i + 1 < args.Length: task = args[++i]; break;
                case "--runs"      when i + 1 < args.Length: runs = int.Parse(args[++i]); break;
                case "--out"       when i + 1 < args.Length: outPath = args[++i]; break;
                default:
                    Console.Error.WriteLine($"experiment judge-repeat: unknown argument: {args[i]}");
                    PrintUsage();
                    return 64;
            }
        }

        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
        {
            Console.Error.WriteLine("experiment judge-repeat: --workspace is required and must exist (use --keep-workspace on a prior run to produce one)");
            return 64;
        }
        if (string.IsNullOrWhiteSpace(task))
        {
            Console.Error.WriteLine("experiment judge-repeat: --task is required");
            return 64;
        }
        if (runs < 1)
        {
            Console.Error.WriteLine("experiment judge-repeat: --runs must be >= 1");
            return 64;
        }

        var repoRoot = Program.FindRepoRoot()
            ?? throw new InvalidOperationException("Could not locate repo root (looking for AML-Agent-Bench.sln)");
        var agentProj = Path.Combine(repoRoot, "agents", "csharp-sk", "AmlAgent.csproj");
        var judgeReportPath = Path.Combine(workspace, "judge_report.json");

        var records = new List<JsonObject>();
        for (int run = 1; run <= runs; run++)
        {
            Console.WriteLine($"[experiment judge-repeat] run {run}/{runs} against {workspace}");
            var record = JudgeOnce(agentProj, task, workspace, judgeReportPath, run);
            records.Add(record);
            Console.WriteLine($"[experiment judge-repeat]   verdict={(string?)record["verdict"] ?? "?"} overall_percentage={(double?)record["overall_percentage"]}");
        }

        var overallPercentages = records.Select(r => (double?)r["overall_percentage"]).Where(v => v is not null).Select(v => v!.Value).ToList();
        var verdicts = records.Select(r => (string?)r["verdict"]).Where(v => v is not null).Distinct().ToList();

        var result = new JsonObject
        {
            ["experiment_type"] = "judge_repeatability",
            ["workspace"] = workspace,
            ["task"] = task,
            ["requested_runs"] = runs,
            ["generated_at_utc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["note"] = "Raw per-run judge outputs only. distinct_verdicts_observed/overall_percentage_range are simple descriptive summaries of the raw data below, not a claimed reliability statistic (e.g. no variance/Kappa is computed).",
            ["distinct_verdicts_observed"] = new JsonArray(verdicts.Select(v => (JsonNode)v!).ToArray()),
            ["overall_percentage_min"] = overallPercentages.Count > 0 ? overallPercentages.Min() : null,
            ["overall_percentage_max"] = overallPercentages.Count > 0 ? overallPercentages.Max() : null,
            ["runs"] = new JsonArray(records.Select(r => (JsonNode)r).ToArray()),
        };

        File.WriteAllText(outPath, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[experiment judge-repeat] wrote {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static JsonObject JudgeOnce(string agentProj, string task, string workspace, string judgeReportPath, int runIndex)
    {
        var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "run", "--project", agentProj, "--no-build", "--", "judge", "--task", task, "--workspace", workspace })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        _ = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        var record = new JsonObject { ["run_index"] = runIndex, ["exit_code"] = p.ExitCode };

        if (!File.Exists(judgeReportPath))
        {
            record["error"] = "judge_report.json was not produced";
            record["stderr_tail"] = Program.RedactForLog(stderr.Length > 2000 ? stderr[^2000..] : stderr);
            return record;
        }

        // Captured immediately, before the next run overwrites judge_report.json --
        // every individual result is preserved in full, per the instructions.
        var fullReport = JsonNode.Parse(File.ReadAllText(judgeReportPath))!.AsObject();
        record["overall_percentage"] = fullReport["overall_percentage"]?.DeepClone();
        record["verdict"] = fullReport["verdict"]?.DeepClone();
        record["dimension_scores"] = fullReport["scores"]?.DeepClone() ?? fullReport["dimension_scores"]?.DeepClone();
        record["claims"] = fullReport["claims"]?.DeepClone();
        record["eghr"] = fullReport["eghr"]?.DeepClone();
        record["evidence_traceability"] = fullReport["evidence_traceability"]?.DeepClone();
        record["full_judge_report"] = fullReport;

        return record;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("aml-harness experiment judge-repeat --workspace <path> --task <id> [options]");
        Console.WriteLine();
        Console.WriteLine("Re-scores a FIXED, already-produced agent output N times with the same judge");
        Console.WriteLine("configuration, to measure LLM-as-judge repeatability. Every individual");
        Console.WriteLine("judge_report.json is captured in full, not just an average.");
        Console.WriteLine();
        Console.WriteLine("  --workspace <path>   an existing workspace with an agent's output already in it");
        Console.WriteLine("                       (e.g. from a prior `aml-harness --keep-workspace` run)");
        Console.WriteLine("  --task <id>          task id (for rubric.json lookup)");
        Console.WriteLine("  --runs <n>           repetition count (default: 5)");
        Console.WriteLine("  --out <path>         output path (default: judge_repeatability_result.json)");
        Console.WriteLine();
        Console.WriteLine("Requires OPENAI_API_KEY -- each run is a genuine judge invocation.");
        Console.WriteLine("Exit codes: 0 = completed (see individual run records for per-run outcomes), 64 = usage error.");
    }
}
