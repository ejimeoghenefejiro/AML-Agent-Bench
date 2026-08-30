using System.Text.Json.Nodes;

namespace AmlAgent.Evidence;

/// <summary>
/// Compares Condition A (free narrative citations, the LLM judge maps claims
/// to evidence from prose) against Condition B (structured citation output,
/// the agent declares its own claim-to-evidence mapping directly) for the
/// SAME task/agent/model, from their two judge_report.json files (v0.3
/// validation-priorities item 4). This is RQ4's own question made concrete:
/// does a structured output contract improve reference validity, claim-level
/// precision/recall, Claim Support Coverage, and extraction reliability, the
/// way docs/experimental-design.md's intervention framing predicts it might?
///
/// Raw comparison only, same discipline as AmlAgent.Evidence.JudgeVsHumanComparison
/// and ClaimAnnotationAdjudication -- this never itself concludes "structured
/// output is better", only reports what changed, on however many runs were
/// actually compared. A real answer to the RQ4 question needs repeated runs
/// across both conditions (see docs/experimental-design.md), not a
/// single-run diff.
/// </summary>
public static class StructuredOutputConditionComparison
{
    /// <summary>
    /// Compares two judge_report.json documents (parsed as JsonObject) for
    /// the same task/agent/model, one run under each condition. Throws if
    /// either report's own extraction method doesn't match the condition it
    /// was passed as -- this comparison is meaningless if the two inputs
    /// aren't actually one run of each condition.
    /// </summary>
    public static StructuredOutputComparisonResult Compare(JsonObject conditionAReport, JsonObject conditionBReport)
    {
        var methodA = (string?)conditionAReport["evidence_extraction_method"];
        var methodB = (string?)conditionBReport["evidence_extraction_method"];

        if (methodA is not null && methodA != "llm_mapped_from_narrative")
            throw new ArgumentException($"conditionAReport's evidence_extraction_method is '{methodA}', expected 'llm_mapped_from_narrative' -- wrong report passed as Condition A");
        if (methodB is not null && methodB != "structured_output")
            throw new ArgumentException($"conditionBReport's evidence_extraction_method is '{methodB}', expected 'structured_output' -- wrong report passed as Condition B");

        var traceA = conditionAReport["evidence_traceability"]?.AsObject();
        var traceB = conditionBReport["evidence_traceability"]?.AsObject();

        return new StructuredOutputComparisonResult(
            ReferenceValidityRateA: ReferenceValidityRate(traceA),
            ReferenceValidityRateB: ReferenceValidityRate(traceB),
            ClaimSupportCoverageA: (double?)conditionAReport["claim_support_coverage"],
            ClaimSupportCoverageB: (double?)conditionBReport["claim_support_coverage"],
            ClaimLevelPrecisionA: (double?)conditionAReport["claim_level_precision"],
            ClaimLevelPrecisionB: (double?)conditionBReport["claim_level_precision"],
            ClaimLevelRecallA: (double?)conditionAReport["claim_level_recall"],
            ClaimLevelRecallB: (double?)conditionBReport["claim_level_recall"],
            MaterialClaimCountA: conditionAReport["material_claims"]?.AsArray()?.Count ?? 0,
            MaterialClaimCountB: conditionBReport["material_claims"]?.AsArray()?.Count ?? 0);
    }

    private static double? ReferenceValidityRate(JsonObject? trace)
    {
        var citedDistinct = (int?)trace?["cited_txn_ids_distinct"];
        var fabricatedCount = trace?["fabricated_citations"]?.AsArray()?.Count ?? 0;
        return citedDistinct is int cd && cd > 0 ? Math.Round((double)(cd - fabricatedCount) / cd, 4) : null;
    }
}

/// <summary>
/// Raw side-by-side comparison of the two conditions -- every field is A and
/// B individually; no single "condition B is better" verdict is computed.
/// Delta properties are provided as a convenience (positive = B higher than A).
/// </summary>
public sealed record StructuredOutputComparisonResult(
    double? ReferenceValidityRateA, double? ReferenceValidityRateB,
    double? ClaimSupportCoverageA, double? ClaimSupportCoverageB,
    double? ClaimLevelPrecisionA, double? ClaimLevelPrecisionB,
    double? ClaimLevelRecallA, double? ClaimLevelRecallB,
    int MaterialClaimCountA, int MaterialClaimCountB)
{
    public double? ReferenceValidityRateDelta => Delta(ReferenceValidityRateA, ReferenceValidityRateB);
    public double? ClaimSupportCoverageDelta => Delta(ClaimSupportCoverageA, ClaimSupportCoverageB);
    public double? ClaimLevelPrecisionDelta => Delta(ClaimLevelPrecisionA, ClaimLevelPrecisionB);
    public double? ClaimLevelRecallDelta => Delta(ClaimLevelRecallA, ClaimLevelRecallB);

    private static double? Delta(double? a, double? b) => a is null || b is null ? null : Math.Round(b.Value - a.Value, 4);
}
