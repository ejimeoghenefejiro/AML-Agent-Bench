using System.Text.Json;
using System.Text.Json.Nodes;
using AmlAgent.Evidence;

namespace AmlAgent.Harness;

/// <summary>
/// `aml-harness compare &lt;profile1.json&gt; &lt;profile2.json&gt; ...` --
/// CLI-only multi-agent comparison (CLI-Only Assurance Roadmap item 2/9).
/// Reads two or more assurance_profile.json files and prints a side-by-side
/// table, so a bank-style "which agent is actually safer" comparison never
/// needs a UI. Never invents a value for a not_implemented dimension --
/// only the four metrics this benchmark actually measures are shown; the
/// rest stay visibly absent.
/// </summary>
internal static class CompareCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("aml-harness compare <profile1.json> <profile2.json> [...]");
            Console.WriteLine();
            Console.WriteLine("Prints a side-by-side comparison of two or more assurance_profile.json files");
            Console.WriteLine("and writes comparison_result.json with the same data in machine-readable form.");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 = compared successfully, 6 = invalid comparison (file not found / malformed / no comparable data).");
            return 0;
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("compare needs at least two assurance_profile.json paths");
            return 6;
        }

        var profiles = new List<(string Path, JsonObject Profile)>();
        foreach (var path in args)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"compare: file not found: {path}");
                return 6;
            }

            try
            {
                var profile = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                    ?? throw new InvalidDataException("empty JSON");
                profiles.Add((path, profile));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"compare: {path} is not a valid assurance_profile.json: {ex.Message}");
                return 6;
            }
        }

        var identities = profiles.Select(p => ToRunIdentity(p.Path, p.Profile)).ToList();
        var warnings = CompatibilityCheck.Check(identities);
        foreach (var w in warnings)
            Console.Error.WriteLine($"[compare] WARNING: {w}");
        if (warnings.Count > 0)
            Console.WriteLine("(comparison below spans non-equivalent runs -- see warnings above)");

        var rows = profiles.Select(p => ExtractRow(p.Profile)).ToList();
        Program.PrintTable("Assurance comparison", HeaderRow, rows);

        WriteMachineReadable(profiles, warnings);
        return 0;
    }

    private static RunIdentity ToRunIdentity(string path, JsonObject profile) => new(
        Label: (string?)profile["agent"]?["name"] ?? path,
        Task: (string?)profile["scenario_pack"],
        PolicyId: (string?)profile["policy"]?["id"],
        PolicyVersion: (string?)profile["policy"]?["version"],
        BenchmarkVersion: (string?)profile["provenance"]?["benchmark_version"],
        DatasetHash: (string?)profile["provenance"]?["dataset_hash"],
        RequiredDimensionsFingerprint: string.Join(',',
            (profile["metrics"]?.AsArray() ?? new JsonArray())
                .Where(m => (bool?)m?["required"] != false)
                .Select(m => (string?)m?["metric"])
                .OrderBy(s => s, StringComparer.Ordinal)));

    private static readonly string[] HeaderRow =
    {
        "Agent", "Model", "Task", "Policy", "Task Perf.", "EGHR", "Precision", "Recall", "Trace F1", "Fabricated", "Decision",
    };

    private static string[] ExtractRow(JsonObject profile)
    {
        var agentName = (string?)profile["agent"]?["name"] ?? "?";
        var model = (string?)profile["agent"]?["model"] ?? "?";
        var task = (string?)profile["scenario_pack"] ?? "?";
        var policyId = (string?)profile["policy"]?["id"] ?? "?";
        var policyVersion = (string?)profile["policy"]?["version"] ?? "?";
        var metrics = profile["metrics"]?.AsArray();
        var trace = profile["evidence_summary"]?["evidence_traceability"]?.AsObject();
        var status = profile["status_summary"]?.AsObject();

        double? Find(string metric) =>
            (double?)metrics?.FirstOrDefault(m => (string?)m?["metric"] == metric)?["value"];

        var taskPerf = Find("task_performance_percentage");
        var eghr = Find("eghr_rate");
        var traceF1 = Find("evidence_traceability_f1");
        var fabricated = Find("fabricated_citation_count");
        var precision = (double?)trace?["precision"];
        var recall = (double?)trace?["recall"];

        string Pct(double? v) => v is double d ? $"{d:P1}" : "n/a";

        return new[]
        {
            agentName,
            model,
            task,
            $"{policyId} v{policyVersion}",
            Pct(taskPerf),
            Pct(eghr),
            Pct(precision),
            Pct(recall),
            Pct(traceF1),
            fabricated is double fc ? fc.ToString("0") : "n/a",
            (string?)status?["assurance_decision"] ?? "-",
        };
    }

    private static void WriteMachineReadable(List<(string Path, JsonObject Profile)> profiles, IReadOnlyList<string> warnings)
    {
        var comparedDims = new[] { "task_performance_percentage", "eghr_rate", "evidence_traceability_precision", "evidence_traceability_recall", "evidence_traceability_f1", "fabricated_citation_count" };
        var excludedDims = profiles.SelectMany(p => p.Profile["not_evaluated_dimensions"]?.AsArray()?.Select(d => (string?)d) ?? Array.Empty<string?>())
            .Where(d => d is not null).Distinct().ToList();

        var result = new JsonObject
        {
            ["generated_at_utc"] = DateTime.UtcNow.ToString("o"),
            ["compared_runs"] = new JsonArray(profiles.Select(p => (JsonNode)new JsonObject
            {
                ["path"] = p.Path,
                ["agent"] = (string?)p.Profile["agent"]?["name"],
                ["model"] = (string?)p.Profile["agent"]?["model"],
                ["task"] = (string?)p.Profile["scenario_pack"],
                ["policy_id"] = (string?)p.Profile["policy"]?["id"],
                ["policy_version"] = (string?)p.Profile["policy"]?["version"],
                ["assurance_decision"] = (string?)p.Profile["status_summary"]?["assurance_decision"],
                ["run_id"] = (string?)p.Profile["provenance"]?["run_id"],
            }).ToArray()),
            ["comparable_dimensions"] = new JsonArray(comparedDims.Select(d => (JsonNode)d).ToArray()),
            ["excluded_dimensions"] = new JsonArray(excludedDims.Select(d => (JsonNode)d!).ToArray()),
            ["warnings"] = new JsonArray(warnings.Select(w => (JsonNode)w).ToArray()),
        };

        var path = "comparison_result.json";
        File.WriteAllText(path, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[compare] wrote {Path.GetFullPath(path)}");
    }
}
