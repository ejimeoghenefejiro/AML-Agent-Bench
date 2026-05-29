using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace AmlAgent.Harness;

/// <summary>
/// Consolidates everything one benchmark run produced into a single
/// <c>bench_result.json</c> in the workspace, plus an archival timestamped
/// copy under <c>results/</c> at the repository root.
///
/// The consolidated record contains:
///   - run metadata (task, agent, model, mode, timing)
///   - the agent's structured output files (parsed when possible)
///   - the xUnit verdict (totals, per-test failures with messages)
///   - the LLM-judge report (per-dimension scores and overall)
///   - the harness's overall PASS / FAIL verdict and a human reason string
///
/// One JSON per run = easy to aggregate across runs into the empirical study
/// the PhD's first-year objective calls for.
/// </summary>
internal static class ReportBuilder
{
    public sealed record RunMeta(
        string RunId, DateTime StartedUtc, DateTime CompletedUtc,
        string Task, string AgentSource, string AgentName, string? Model,
        int MaxSteps, string Mode);

    public sealed record HarnessOutcomes(
        int AgentExitCode, int XUnitExitCode, int JudgeExitCode,
        bool JudgeWasRun);

    public static void Build(
        string workspace,
        string repoRoot,
        RunMeta meta,
        HarnessOutcomes outcomes,
        string? trxPath)
    {
        var root = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["run_id"] = meta.RunId,
            ["started_at_utc"] = meta.StartedUtc.ToString("o"),
            ["completed_at_utc"] = meta.CompletedUtc.ToString("o"),
            ["elapsed_seconds"] = Math.Round((meta.CompletedUtc - meta.StartedUtc).TotalSeconds, 2),
            ["task"] = meta.Task,
            ["agent"] = new JsonObject
            {
                ["source"] = meta.AgentSource,
                ["name"] = meta.AgentName,
                ["model"] = meta.Model,
                ["max_steps"] = meta.MaxSteps,
                ["mode"] = meta.Mode,
            },
            ["workspace"] = workspace,
            ["agent_exit_code"] = outcomes.AgentExitCode,
            ["agent_outputs"] = CollectAgentOutputs(workspace),
            ["xunit"] = ParseTrx(trxPath, outcomes.XUnitExitCode),
            ["judge"] = ReadJudgeReport(workspace, outcomes.JudgeWasRun, outcomes.JudgeExitCode),
        };

        var overallPass = outcomes.XUnitExitCode == 0
                       && (!outcomes.JudgeWasRun || outcomes.JudgeExitCode == 0);
        root["overall_verdict"] = overallPass ? "PASS" : "FAIL";
        root["overall_reason"] = SummariseReason(root);

        var serialised = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // Workspace copy (always)
        var inWorkspace = Path.Combine(workspace, "bench_result.json");
        File.WriteAllText(inWorkspace, serialised);
        Console.WriteLine($"[harness] wrote {inWorkspace}");

        // Archival copy at repo root (gitignored — for cross-run aggregation)
        try
        {
            var resultsDir = Path.Combine(repoRoot, "results");
            Directory.CreateDirectory(resultsDir);
            var stamp = meta.StartedUtc.ToString("yyyyMMdd-HHmmss");
            var safeName = $"{stamp}-{meta.Task}-{meta.AgentName}.json";
            var inResults = Path.Combine(resultsDir, safeName);
            File.WriteAllText(inResults, serialised);
            Console.WriteLine($"[harness] archived  {inResults}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harness] could not write results/ archival copy: {ex.Message}");
        }
    }

    private static JsonObject CollectAgentOutputs(string workspace)
    {
        // Limit to the most useful artefacts; skip the staged task-brief files
        // and the data folder (which the agent did not author).
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "instruction.md", "prompt.md", "expected-behaviour.md", "tests.md",
            "judge_report.json", "bench_result.json",
        };

        var outputs = new JsonObject();
        foreach (var path in Directory.GetFiles(workspace))
        {
            var name = Path.GetFileName(path);
            if (skip.Contains(name)) continue;

            var size = new FileInfo(path).Length;
            var entry = new JsonObject
            {
                ["size_bytes"] = size,
            };

            if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                entry["rows"] = ParseCsv(path);
            }
            else if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                var content = File.ReadAllText(path);
                entry["content_preview"] = content.Length <= 4000
                    ? content
                    : content[..4000] + "\n...[truncated]";
                entry["citation_count"] = System.Text.RegularExpressions.Regex
                    .Matches(content, @"\bT[123]-\d{3}\b").Count;
            }
            outputs[name] = entry;
        }
        return outputs;
    }

    private static JsonArray ParseCsv(string path)
    {
        var rows = new JsonArray();
        var lines = File.ReadAllLines(path)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();
        if (lines.Count < 1) return rows;

        var headers = lines[0].Split(',');
        for (int i = 1; i < lines.Count; i++)
        {
            var cells = lines[i].Split(',');
            var row = new JsonObject();
            for (int c = 0; c < headers.Length && c < cells.Length; c++)
            {
                var raw = cells[c];
                JsonNode? value;
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
                    && raw.IndexOf('-') != 0 + raw.IndexOf('T') && !raw.Contains('-'))
                {
                    // Round-trip via decimal-ish path: keep as number when it parses
                    // and doesn't look like an ISO date (which can also parse as
                    // a number in some locales).
                    value = num;
                }
                else
                {
                    value = raw;
                }
                row[headers[c]] = value;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static JsonObject ParseTrx(string? trxPath, int exitCode)
    {
        var result = new JsonObject
        {
            ["exit_code"] = exitCode,
            ["verdict"] = exitCode == 0 ? "PASS" : "FAIL",
        };

        if (string.IsNullOrEmpty(trxPath) || !File.Exists(trxPath))
        {
            result["trx_present"] = false;
            return result;
        }

        try
        {
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            var doc = XDocument.Load(trxPath);

            var summary = doc.Descendants(ns + "ResultSummary").FirstOrDefault();
            var counters = summary?.Element(ns + "Counters");
            if (counters != null)
            {
                result["total"] = (int?)counters.Attribute("total") ?? 0;
                result["passed"] = (int?)counters.Attribute("passed") ?? 0;
                result["failed"] = (int?)counters.Attribute("failed") ?? 0;
                result["skipped"] = (int?)counters.Attribute("notExecuted") ?? 0;
            }

            var failures = new JsonArray();
            foreach (var r in doc.Descendants(ns + "UnitTestResult"))
            {
                var outcome = (string?)r.Attribute("outcome");
                if (outcome != "Failed") continue;
                var name = (string?)r.Attribute("testName");
                var err = r.Element(ns + "Output")?.Element(ns + "ErrorInfo");
                failures.Add(new JsonObject
                {
                    ["test_name"] = name,
                    ["duration"] = (string?)r.Attribute("duration"),
                    ["message"] = (string?)err?.Element(ns + "Message"),
                    ["stack_trace"] = (string?)err?.Element(ns + "StackTrace"),
                });
            }
            result["failures"] = failures;
            result["trx_present"] = true;
        }
        catch (Exception ex)
        {
            result["trx_parse_error"] = ex.Message;
        }
        return result;
    }

    private static JsonNode? ReadJudgeReport(string workspace, bool judgeWasRun, int judgeExitCode)
    {
        if (!judgeWasRun)
        {
            return new JsonObject { ["was_run"] = false };
        }
        var path = Path.Combine(workspace, "judge_report.json");
        if (!File.Exists(path))
        {
            return new JsonObject
            {
                ["was_run"] = true,
                ["exit_code"] = judgeExitCode,
                ["report_present"] = false,
            };
        }
        try
        {
            var parsed = JsonNode.Parse(File.ReadAllText(path));
            return parsed;
        }
        catch (Exception ex)
        {
            return new JsonObject { ["parse_error"] = ex.Message };
        }
    }

    private static string SummariseReason(JsonObject root)
    {
        var parts = new List<string>();
        var xunit = root["xunit"]?.AsObject();
        if (xunit != null)
        {
            var v = (string?)xunit["verdict"];
            var failed = (int?)xunit["failed"];
            if (v == "PASS")
                parts.Add($"xUnit PASS ({xunit["passed"]}/{xunit["total"]})");
            else
                parts.Add($"xUnit FAIL ({failed} assertion(s))");
        }
        var judge = root["judge"]?.AsObject();
        if (judge != null && judge["verdict"] != null)
        {
            var v = (string?)judge["verdict"];
            var pct = (double?)judge["overall_percentage"] ?? 0.0;
            parts.Add($"judge {v} at {pct:P1}");
        }
        return string.Join("; ", parts);
    }
}
