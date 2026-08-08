namespace AmlAgent.Evidence;

/// <summary>
/// Pure comparison logic for the CLI-Only Assurance Roadmap's "compare" and
/// "regress" commands (items 9 and 10): ranking assurance decisions so a
/// regression can be detected programmatically, and diffing two runs'
/// metric values into a reviewable delta. No I/O -- callers (the harness's
/// CompareCommand/RegressCommand) load the assurance_profile.json files and
/// pass plain values in, so this stays unit-testable without a workspace.
/// </summary>
public static class AssuranceComparison
{
    private static readonly IReadOnlyDictionary<string, int> DecisionRanks = new Dictionary<string, int>
    {
        ["PASS"] = 0,
        ["PASS_WITH_CONDITIONS"] = 1,
        ["NOT_READY_FOR_DEPLOYMENT"] = 2,
    };

    /// <summary>Higher = worse. Unknown decision strings rank worst of all, so an unrecognised value is never silently treated as safe.</summary>
    public static int DecisionRank(string decision) =>
        DecisionRanks.TryGetValue(decision, out var rank) ? rank : int.MaxValue;

    /// <summary>True if the candidate's decision is strictly worse than the baseline's.</summary>
    public static bool IsRegression(string baselineDecision, string candidateDecision) =>
        DecisionRank(candidateDecision) > DecisionRank(baselineDecision);

    /// <summary>
    /// Compares one metric between two runs. "Better"/"worse" is relative to
    /// the metric's own direction (e.g. EGHR going up is worse; traceability
    /// F1 going up is better) so the sign of Change alone isn't enough to
    /// read a delta as good or bad without also knowing direction.
    /// </summary>
    public static MetricDelta CompareMetric(string metric, string label, string? direction, double? baseline, double? candidate)
    {
        double? change = (baseline is double b && candidate is double c) ? c - b : null;

        string trend = "unknown";
        if (change is double d && direction is not null)
        {
            if (Math.Abs(d) < 1e-9) trend = "unchanged";
            else if (direction == "lower_is_better") trend = d > 0 ? "worse" : "better";
            else if (direction == "higher_is_better") trend = d > 0 ? "better" : "worse";
        }

        return new MetricDelta(metric, label, baseline, candidate, change, trend);
    }
}

/// <summary>One metric's value in a baseline run, in a candidate run, and the change between them.</summary>
public sealed record MetricDelta(
    string Metric, string Label, double? Baseline, double? Candidate, double? Change, string Trend);
