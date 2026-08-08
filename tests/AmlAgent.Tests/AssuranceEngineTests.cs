using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.AssuranceEngine -- the pure decision
/// logic behind the assurance-profile prototype (see assurance/README.md).
/// Always-on: no workspace, no LLM, no policy file needed.
/// </summary>
public class AssuranceEngineTests
{
    private static MetricThreshold LowerIsBetter(string metric, double threshold) =>
        new(metric, metric, "lower_is_better", threshold, "rate");

    private static MetricThreshold HigherIsBetter(string metric, double threshold) =>
        new(metric, metric, "higher_is_better", threshold, "rate");

    [Fact]
    public void EvaluateMetric_HigherIsBetter_PassesAtOrAboveThreshold()
    {
        var t = HigherIsBetter("f1", 0.90);
        Assert.Equal("PASS", AssuranceEngine.EvaluateMetric(t, 0.90).Status);
        Assert.Equal("PASS", AssuranceEngine.EvaluateMetric(t, 0.95).Status);
        Assert.Equal("FAIL", AssuranceEngine.EvaluateMetric(t, 0.89).Status);
    }

    [Fact]
    public void EvaluateMetric_LowerIsBetter_PassesAtOrBelowThreshold()
    {
        var t = LowerIsBetter("eghr", 0.05);
        Assert.Equal("PASS", AssuranceEngine.EvaluateMetric(t, 0.05).Status);
        Assert.Equal("PASS", AssuranceEngine.EvaluateMetric(t, 0.0).Status);
        Assert.Equal("FAIL", AssuranceEngine.EvaluateMetric(t, 0.06).Status);
    }

    [Fact]
    public void EvaluateMetric_NullValue_IsNotEvaluated()
    {
        var t = HigherIsBetter("f1", 0.90);
        var result = AssuranceEngine.EvaluateMetric(t, null);
        Assert.Equal("NOT_EVALUATED", result.Status);
    }

    [Fact]
    public void Decide_AllPassNoGaps_ReturnsPass()
    {
        var results = new[]
        {
            AssuranceEngine.EvaluateMetric(LowerIsBetter("eghr", 0.05), 0.02),
            AssuranceEngine.EvaluateMetric(HigherIsBetter("f1", 0.90), 0.96),
        };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("PASS", decision.Overall);
        Assert.Empty(decision.NotEvaluatedDimensions);
    }

    [Fact]
    public void Decide_AllPassButDimensionsNotImplemented_ReturnsPassWithConditions()
    {
        var results = new[]
        {
            AssuranceEngine.EvaluateMetric(LowerIsBetter("eghr", 0.05), 0.02),
        };
        var decision = AssuranceEngine.Decide(results, new[] { "Fairness disparity", "Audit completeness" });
        Assert.Equal("PASS_WITH_CONDITIONS", decision.Overall);
        Assert.Contains("Fairness disparity", decision.NotEvaluatedDimensions);
        Assert.Contains("Audit completeness", decision.NotEvaluatedDimensions);
    }

    [Fact]
    public void Decide_AnyFailedMetric_BlocksDeploymentRegardlessOfOthers()
    {
        var results = new[]
        {
            AssuranceEngine.EvaluateMetric(LowerIsBetter("eghr", 0.05), 0.40), // FAIL
            AssuranceEngine.EvaluateMetric(HigherIsBetter("f1", 0.90), 0.99),  // PASS
        };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", decision.Overall);
        Assert.Contains("eghr", decision.Reason);
    }

    [Fact]
    public void Decide_NothingEvaluated_IsNotReadyForDeployment()
    {
        var results = new[]
        {
            AssuranceEngine.EvaluateMetric(LowerIsBetter("eghr", 0.05), null),
        };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", decision.Overall);
        Assert.Equal(0, decision.EvaluatedCount);
    }

    [Fact]
    public void Decide_MultipleRequiredMetricsFail_AllListedInReason()
    {
        var eghr = new MetricThreshold("eghr_rate", "EGHR", "lower_is_better", 0.05, "rate", Required: true);
        var trace = new MetricThreshold("evidence_traceability_f1", "Evidence Traceability F1", "higher_is_better", 0.90, "rate", Required: true);
        var results = new[]
        {
            AssuranceEngine.EvaluateMetric(eghr, 0.40),
            AssuranceEngine.EvaluateMetric(trace, 0.25),
        };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", decision.Overall);
        Assert.Equal(2, decision.Reasons.Count);
        Assert.Contains("EGHR", decision.Reason);
        Assert.Contains("Evidence Traceability F1", decision.Reason);
    }

    [Fact]
    public void Decide_OneRequiredMetricMissingAmongOthersPresent_DoesNotSilentlyPass()
    {
        var present = HigherIsBetter("f1", 0.90);
        var missing = LowerIsBetter("eghr_rate", 0.05);
        var results = new[]
        {
            AssuranceEngine.EvaluateMetric(present, 0.99),  // PASS
            AssuranceEngine.EvaluateMetric(missing, null),  // NOT_EVALUATED
        };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        // A required metric with no data must never resolve to a bare PASS.
        Assert.NotEqual("PASS", decision.Overall);
        Assert.Contains("eghr_rate", decision.NotEvaluatedDimensions);
    }

    [Fact]
    public void Decide_FabricatedCitationBreach_IsARequiredFailureThatBlocksDeployment()
    {
        var fabricated = new MetricThreshold("fabricated_citation_count", "Fabricated Citations", "lower_is_better", 0, "count", Required: true);
        var results = new[] { AssuranceEngine.EvaluateMetric(fabricated, 1) }; // any fabrication at all
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", decision.Overall);
        Assert.Equal("critical", decision.Reasons[0].Severity);
        Assert.Equal("Fabricated Citations", decision.Reasons[0].Label);
    }

    [Theory]
    [InlineData(0.05, "PASS")]  // exactly at threshold, lower_is_better -> passes
    [InlineData(0.0500001, "FAIL")] // a hair over -> fails
    public void Decide_BoundaryValue_IsHandledConsistentlyWithEvaluateMetric(double value, string expectedMetricStatus)
    {
        var t = LowerIsBetter("eghr_rate", 0.05);
        var result = AssuranceEngine.EvaluateMetric(t, value);
        Assert.Equal(expectedMetricStatus, result.Status);

        var decision = AssuranceEngine.Decide(new[] { result }, Array.Empty<string>());
        Assert.Equal(expectedMetricStatus == "PASS" ? "PASS" : "NOT_READY_FOR_DEPLOYMENT", decision.Overall);
    }

    [Fact]
    public void Decide_RequiredMetricFailure_BlocksDeployment()
    {
        var required = new MetricThreshold("eghr", "EGHR", "lower_is_better", 0.05, "rate", Required: true);
        var results = new[] { AssuranceEngine.EvaluateMetric(required, 0.40) };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", decision.Overall);
    }

    [Fact]
    public void Decide_OptionalMetricFailure_DowngradesToPassWithConditionsRatherThanBlocking()
    {
        var optional = new MetricThreshold("consistency", "Run Consistency", "higher_is_better", 0.95, "rate", Required: false);
        var required = new MetricThreshold("eghr", "EGHR", "lower_is_better", 0.05, "rate", Required: true);
        var results = new[]
        {
            AssuranceEngine.EvaluateMetric(required, 0.02),   // PASS
            AssuranceEngine.EvaluateMetric(optional, 0.80),   // FAIL, but optional
        };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("PASS_WITH_CONDITIONS", decision.Overall);
    }

    [Fact]
    public void Decide_FailedMetric_ProducesStructuredReasonWithCorrectSeverityAndRule()
    {
        var required = new MetricThreshold("eghr", "EGHR", "lower_is_better", 0.05, "rate", Required: true);
        var results = new[] { AssuranceEngine.EvaluateMetric(required, 0.40) };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());

        var reason = Assert.Single(decision.Reasons);
        Assert.Equal("eghr", reason.Metric);
        Assert.Equal(0.40, reason.Actual);
        Assert.Equal(0.05, reason.Threshold);
        Assert.Equal("maximum", reason.Rule); // lower_is_better -> "must not exceed" -> maximum
        Assert.Equal("critical", reason.Severity);
    }

    [Fact]
    public void Decide_PassingMetrics_ProduceNoReasons()
    {
        var t = HigherIsBetter("f1", 0.90);
        var results = new[] { AssuranceEngine.EvaluateMetric(t, 0.99) };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public void ValidatePolicy_UnknownDirection_IsRejected()
    {
        var bad = new[] { new MetricThreshold("x", "X", "sideways", 0.5, "rate") };
        Assert.Throws<InvalidDataException>(() => AssuranceEngine.ValidatePolicy(bad));
    }

    [Fact]
    public void ValidatePolicy_ImpossibleRateThreshold_IsRejected()
    {
        var tooHigh = new[] { new MetricThreshold("x", "X", "higher_is_better", 1.5, "rate") };
        var negative = new[] { new MetricThreshold("y", "Y", "lower_is_better", -0.1, "rate") };
        Assert.Throws<InvalidDataException>(() => AssuranceEngine.ValidatePolicy(tooHigh));
        Assert.Throws<InvalidDataException>(() => AssuranceEngine.ValidatePolicy(negative));
    }

    [Fact]
    public void ValidatePolicy_ImpossibleCountThreshold_IsRejected()
    {
        var negativeCount = new[] { new MetricThreshold("fabricated", "Fabricated", "lower_is_better", -1, "count") };
        Assert.Throws<InvalidDataException>(() => AssuranceEngine.ValidatePolicy(negativeCount));
    }

    [Fact]
    public void ValidatePolicy_ValidThresholds_DoNotThrow()
    {
        var ok = new[]
        {
            HigherIsBetter("f1", 0.90),
            LowerIsBetter("eghr", 0.05),
            new MetricThreshold("fabricated", "Fabricated", "lower_is_better", 0, "count"),
        };
        AssuranceEngine.ValidatePolicy(ok); // should not throw
    }

    [Fact]
    public void Decide_SameInputAndPolicy_ProducesSameDecision()
    {
        var t = LowerIsBetter("eghr", 0.05);
        var results1 = new[] { AssuranceEngine.EvaluateMetric(t, 0.40) };
        var results2 = new[] { AssuranceEngine.EvaluateMetric(t, 0.40) };
        var d1 = AssuranceEngine.Decide(results1, Array.Empty<string>());
        var d2 = AssuranceEngine.Decide(results2, Array.Empty<string>());
        Assert.Equal(d1.Overall, d2.Overall);
        Assert.Equal(d1.Reason, d2.Reason);
    }

    [Fact]
    public void Decide_NotEvaluatedDimensionsAreNeverSilentlyDropped()
    {
        // A metric with no value (NOT_EVALUATED) must surface in
        // NotEvaluatedDimensions just like an explicitly unimplemented one --
        // the whole point is nothing gets hidden.
        var results = new[]
        {
            AssuranceEngine.EvaluateMetric(HigherIsBetter("f1", 0.90), null),
        };
        var decision = AssuranceEngine.Decide(results, new[] { "Audit completeness" });
        Assert.Contains("f1", decision.NotEvaluatedDimensions);
        Assert.Contains("Audit completeness", decision.NotEvaluatedDimensions);
    }

    // -- Case-level evidence integrity gate (Priority 6: distinguishes agent-level
    // hallucination/fabricated-citation/missing-gold-evidence, already covered above
    // via judge metrics, from case-level "was the evidence itself trustworthy") --

    [Fact]
    public void EvaluateCaseIntegrity_CaseNotPresent_ProducesNoReasons()
    {
        var assessment = AssuranceEngine.EvaluateCaseIntegrity(casePresent: false, invalidSourceEvidenceReferenceCount: 0, brokenCanonicalEvidenceLineageCount: 0);
        Assert.False(assessment.Present);
        Assert.Empty(assessment.Reasons);
    }

    [Fact]
    public void EvaluateCaseIntegrity_CasePresentAndClean_ProducesNoReasons()
    {
        var assessment = AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 0, brokenCanonicalEvidenceLineageCount: 0);
        Assert.True(assessment.Present);
        Assert.Empty(assessment.Reasons);
    }

    [Fact]
    public void EvaluateCaseIntegrity_InvalidSourceEvidenceReference_ProducesDistinctCriticalReason()
    {
        var assessment = AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 2, brokenCanonicalEvidenceLineageCount: 0);
        var reason = Assert.Single(assessment.Reasons);
        Assert.Equal("case_evidence_integrity.invalid_source_evidence_reference", reason.Metric);
        Assert.Equal(2, reason.Actual);
        Assert.Equal("critical", reason.Severity);
    }

    [Fact]
    public void EvaluateCaseIntegrity_BrokenCanonicalEvidenceLineage_ProducesDistinctCriticalReason()
    {
        var assessment = AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 0, brokenCanonicalEvidenceLineageCount: 1);
        var reason = Assert.Single(assessment.Reasons);
        Assert.Equal("case_evidence_integrity.broken_canonical_evidence_lineage", reason.Metric);
        Assert.Equal(1, reason.Actual);
        Assert.Equal("critical", reason.Severity);
    }

    [Fact]
    public void EvaluateCaseIntegrity_BothFailureKinds_ProducesTwoDistinctReasons()
    {
        // The whole point of Priority 6: an invalid reference and a broken lineage
        // are different failure kinds and must be individually visible, not merged
        // into one generic "case has problems" flag.
        var assessment = AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 1, brokenCanonicalEvidenceLineageCount: 1);
        Assert.Equal(2, assessment.Reasons.Count);
        Assert.Contains(assessment.Reasons, r => r.Metric == "case_evidence_integrity.invalid_source_evidence_reference");
        Assert.Contains(assessment.Reasons, r => r.Metric == "case_evidence_integrity.broken_canonical_evidence_lineage");
    }

    [Fact]
    public void ApplyCaseIntegrityGate_NoCaseReasons_LeavesDecisionUntouched()
    {
        var results = new[] { AssuranceEngine.EvaluateMetric(HigherIsBetter("f1", 0.90), 0.95) };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        var clean = AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 0, brokenCanonicalEvidenceLineageCount: 0);

        var gated = AssuranceEngine.ApplyCaseIntegrityGate(decision, clean);

        Assert.Equal(decision.Overall, gated.Overall);
        Assert.Equal(decision.Reason, gated.Reason);
        Assert.Equal(decision.Reasons.Count, gated.Reasons.Count);
    }

    [Fact]
    public void ApplyCaseIntegrityGate_CaseIntegrityFailure_ForcesNotReadyForDeploymentEvenWithPassingMetrics()
    {
        // This is the core requirement: "a benchmark should not be considered
        // assurance-valid if the underlying canonical case itself has unresolved
        // evidence-integrity failures" -- regardless of how well the agent scored.
        var results = new[] { AssuranceEngine.EvaluateMetric(HigherIsBetter("f1", 0.90), 1.0) };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("PASS", decision.Overall);

        var failing = AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 1, brokenCanonicalEvidenceLineageCount: 0);
        var gated = AssuranceEngine.ApplyCaseIntegrityGate(decision, failing);

        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", gated.Overall);
        Assert.Contains(gated.Reasons, r => r.Metric == "case_evidence_integrity.invalid_source_evidence_reference");
    }

    [Fact]
    public void ApplyCaseIntegrityGate_CaseIntegrityFailure_PreservesOriginalMetricReasonsToo()
    {
        // Transparency: gating to NOT_READY_FOR_DEPLOYMENT must not hide what the
        // judge metrics themselves said -- both sets of reasons coexist.
        var results = new[] { AssuranceEngine.EvaluateMetric(HigherIsBetter("f1", 0.90), 1.0) };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        var failing = AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 0, brokenCanonicalEvidenceLineageCount: 1);

        var gated = AssuranceEngine.ApplyCaseIntegrityGate(decision, failing);

        Assert.Equal(decision.Reasons.Count + 1, gated.Reasons.Count);
    }

    [Fact]
    public void ApplyCaseIntegrityGate_AlreadyNotReadyForDeployment_KeepsOriginalReasonText()
    {
        // If the metrics already failed a required threshold, the case-integrity
        // gate must not overwrite that explanation with its own -- both problems
        // are real and the original reason stays primary.
        var results = new[] { AssuranceEngine.EvaluateMetric(HigherIsBetter("f1", 0.90), 0.1) };
        var decision = AssuranceEngine.Decide(results, Array.Empty<string>());
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", decision.Overall);

        var failing = AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 1, brokenCanonicalEvidenceLineageCount: 0);
        var gated = AssuranceEngine.ApplyCaseIntegrityGate(decision, failing);

        Assert.Equal(decision.Reason, gated.Reason);
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", gated.Overall);
    }
}
