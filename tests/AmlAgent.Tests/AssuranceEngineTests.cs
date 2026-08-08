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
}
