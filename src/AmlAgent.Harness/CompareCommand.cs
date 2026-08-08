using System.Text.Json.Nodes;

namespace AmlAgent.Harness;

/// <summary>
/// `aml-harness compare &lt;profile1.json&gt; &lt;profile2.json&gt; ...` --
/// CLI-only multi-agent comparison (CLI-Only Assurance Roadmap item 9).
/// Reads two or more assurance_profile.json files (from assurance-profiles/
/// or a kept workspace) and prints a side-by-side table, so a bank-style
/// "which agent is actually safer" comparison never needs a UI.
/// </summary>
internal static class CompareCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("aml-harness compare <profile1.json> <profile2.json> [...]");
            Console.WriteLine();
            Console.WriteLine("Prints a side-by-side comparison of two or more assurance_profile.json files.");
            return 0;
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("compare needs at least two assurance_profile.json paths");
            return 64;
        }

        var rows = new List<string[]>();
        foreach (var path in args)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"compare: file not found: {path}");
                return 1;
            }

            JsonObject profile;
            try
            {
                profile = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                    ?? throw new InvalidDataException("empty JSON");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"compare: {path} is not a valid assurance_profile.json: {ex.Message}");
                return 1;
            }

            rows.Add(ExtractRow(profile));
        }

        Program.PrintTable("Assurance comparison", new[] { "Agent", "Task Perf.", "EGHR", "Trace F1", "Fabricated", "Decision" }, rows);
        return 0;
    }

    private static string[] ExtractRow(JsonObject profile)
    {
        var agentName = (string?)profile["agent"]?["name"] ?? "?";
        var metrics = profile["metrics"]?.AsArray();
        var status = profile["status_summary"]?.AsObject();

        double? Find(string metric) =>
            (double?)metrics?.FirstOrDefault(m => (string?)m?["metric"] == metric)?["value"];

        var taskPerf = Find("task_performance_percentage");
        var eghr = Find("eghr_rate");
        var traceF1 = Find("evidence_traceability_f1");
        var fabricated = Find("fabricated_citation_count");

        return new[]
        {
            agentName,
            taskPerf is double tp ? $"{tp:P1}" : "n/a",
            eghr is double e ? $"{e:P1}" : "n/a",
            traceF1 is double f ? $"{f:P1}" : "n/a",
            fabricated is double fc ? fc.ToString("0") : "n/a",
            (string?)status?["assurance_decision"] ?? "-",
        };
    }
}
