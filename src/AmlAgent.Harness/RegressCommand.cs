using System.Text.Json.Nodes;
using AmlAgent.Evidence;

namespace AmlAgent.Harness;

/// <summary>
/// `aml-harness regress --baseline &lt;profile.json&gt; --candidate &lt;profile.json&gt;`
/// -- CLI-only continuous-assurance regression detection (CLI-Only Assurance
/// Roadmap item 10). Diffs two assurance_profile.json files metric-by-metric
/// and flags whether the deployment decision got worse, so an agent-version
/// bump can be checked for assurance regressions without any CI/CD pipeline.
/// </summary>
internal static class RegressCommand
{
    public static int Run(string[] args)
    {
        string? baselinePath = null, candidatePath = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baseline"  when i + 1 < args.Length: baselinePath = args[++i]; break;
                case "--candidate" when i + 1 < args.Length: candidatePath = args[++i]; break;
                case "-h" or "--help":
                    Console.WriteLine("aml-harness regress --baseline <profile.json> --candidate <profile.json>");
                    return 0;
            }
        }

        if (baselinePath is null || candidatePath is null)
        {
            Console.Error.WriteLine("regress needs --baseline <path> and --candidate <path>");
            return 64;
        }

        JsonObject baseline, candidate;
        try
        {
            baseline = LoadProfile(baselinePath);
            candidate = LoadProfile(candidatePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"regress: {ex.Message}");
            return 1;
        }

        var baselineMetrics = MetricMap(baseline);
        var candidateMetrics = MetricMap(candidate);
        var directions = MetricDirections(baseline, candidate);
        var units = MetricUnits(baseline, candidate);

        var allMetrics = baselineMetrics.Keys.Union(candidateMetrics.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var deltas = allMetrics.Select(m => AssuranceComparison.CompareMetric(
            m,
            MetricLabel(baseline, candidate, m),
            directions.GetValueOrDefault(m),
            baselineMetrics.GetValueOrDefault(m),
            candidateMetrics.GetValueOrDefault(m))).ToList();

        var baselineDecision = (string?)baseline["status_summary"]?["assurance_decision"] ?? "-";
        var candidateDecision = (string?)candidate["status_summary"]?["assurance_decision"] ?? "-";
        var isRegression = AssuranceComparison.IsRegression(baselineDecision, candidateDecision);

        Console.WriteLine();
        Console.WriteLine(isRegression ? "==== ASSURANCE REGRESSION DETECTED ====" : "==== NO ASSURANCE REGRESSION ====");
        Console.WriteLine();

        var rows = deltas.Select(d => new[]
        {
            d.Label,
            FormatValue(d.Baseline, units.GetValueOrDefault(d.Metric, "rate")),
            FormatValue(d.Candidate, units.GetValueOrDefault(d.Metric, "rate")),
            FormatChange(d.Change, d.Trend, units.GetValueOrDefault(d.Metric, "rate")),
        }).ToList();
        Program.PrintTable("Metric comparison", new[] { "Metric", "Baseline", "Candidate", "Change" }, rows);

        Program.PrintTable("Decision comparison", new[] { "Field", "Value" }, new List<string[]>
        {
            new[] { "Previous decision (baseline)", baselineDecision },
            new[] { "Current decision (candidate)",  candidateDecision },
            new[] { "Regression?", isRegression ? "YES" : "no" },
        });

        return isRegression ? 1 : 0;
    }

    private static JsonObject LoadProfile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"file not found: {path}");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException($"{path} is not a valid assurance_profile.json");
    }

    private static Dictionary<string, double?> MetricMap(JsonObject profile) =>
        (profile["metrics"]?.AsArray() ?? new JsonArray())
            .Where(m => m is not null)
            .ToDictionary(m => (string)m!["metric"]!, m => (double?)m!["value"]);

    private static Dictionary<string, string> MetricDirections(JsonObject baseline, JsonObject candidate)
    {
        var result = new Dictionary<string, string>();
        foreach (var profile in new[] { baseline, candidate })
            foreach (var m in profile["metrics"]?.AsArray() ?? new JsonArray())
                if (m is not null)
                    result[(string)m["metric"]!] = (string?)m["direction"] ?? "";
        return result;
    }

    private static string MetricLabel(JsonObject baseline, JsonObject candidate, string metric)
    {
        foreach (var profile in new[] { baseline, candidate })
        {
            var match = profile["metrics"]?.AsArray()?.FirstOrDefault(m => (string?)m?["metric"] == metric);
            if (match is not null) return (string?)match["label"] ?? metric;
        }
        return metric;
    }

    private static Dictionary<string, string> MetricUnits(JsonObject baseline, JsonObject candidate)
    {
        var result = new Dictionary<string, string>();
        foreach (var profile in new[] { baseline, candidate })
            foreach (var m in profile["metrics"]?.AsArray() ?? new JsonArray())
                if (m is not null)
                    result[(string)m["metric"]!] = (string?)m["unit"] ?? "rate";
        return result;
    }

    private static string FormatValue(double? v, string unit) =>
        v is not double d ? "N/A" : unit == "count" ? d.ToString("0") : $"{d:P1}";

    private static string FormatChange(double? change, string trend, string unit)
    {
        if (change is not double c) return "N/A";
        if (unit == "count")
        {
            var countSign = c >= 0 ? "+" : "";
            return trend == "unchanged" ? "unchanged" : $"{countSign}{c:0} ({trend})";
        }
        var sign = c >= 0 ? "+" : "";
        var pp = $"{sign}{c * 100:0.0} pp";
        return trend switch
        {
            "better" => $"{pp} (better)",
            "worse" => $"{pp} (worse)",
            "unchanged" => "unchanged",
            _ => pp,
        };
    }
}
