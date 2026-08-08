using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.AssuranceComparison -- the pure logic
/// behind the "compare" and "regress" CLI commands. Always-on: no files,
/// no workspace.
/// </summary>
public class AssuranceComparisonTests
{
    [Theory]
    [InlineData("PASS", 0)]
    [InlineData("PASS_WITH_CONDITIONS", 1)]
    [InlineData("NOT_READY_FOR_DEPLOYMENT", 2)]
    public void DecisionRank_OrdersFromBestToWorst(string decision, int expectedRank)
    {
        Assert.Equal(expectedRank, AssuranceComparison.DecisionRank(decision));
    }

    [Fact]
    public void DecisionRank_UnknownDecision_RanksWorstOfAll()
    {
        var rank = AssuranceComparison.DecisionRank("SOMETHING_UNEXPECTED");
        Assert.True(rank > AssuranceComparison.DecisionRank("NOT_READY_FOR_DEPLOYMENT"));
    }

    [Theory]
    [InlineData("PASS", "PASS_WITH_CONDITIONS", true)]
    [InlineData("PASS", "NOT_READY_FOR_DEPLOYMENT", true)]
    [InlineData("PASS_WITH_CONDITIONS", "NOT_READY_FOR_DEPLOYMENT", true)]
    [InlineData("PASS", "PASS", false)]
    [InlineData("NOT_READY_FOR_DEPLOYMENT", "PASS", false)]
    [InlineData("NOT_READY_FOR_DEPLOYMENT", "PASS_WITH_CONDITIONS", false)]
    public void IsRegression_DetectsWorseningOnly(string baseline, string candidate, bool expectedRegression)
    {
        Assert.Equal(expectedRegression, AssuranceComparison.IsRegression(baseline, candidate));
    }

    [Fact]
    public void CompareMetric_LowerIsBetter_IncreaseIsWorse()
    {
        // EGHR going from 2.5% to 8.3% is a regression.
        var delta = AssuranceComparison.CompareMetric("eghr_rate", "EGHR", "lower_is_better", 0.025, 0.083);
        Assert.Equal("worse", delta.Trend);
        Assert.True(delta.Change > 0);
    }

    [Fact]
    public void CompareMetric_HigherIsBetter_DecreaseIsWorse()
    {
        // Evidence traceability F1 going from 96% to 81% is a regression.
        var delta = AssuranceComparison.CompareMetric("evidence_traceability_f1", "Evidence Traceability F1", "higher_is_better", 0.96, 0.81);
        Assert.Equal("worse", delta.Trend);
        Assert.True(delta.Change < 0);
    }

    [Fact]
    public void CompareMetric_HigherIsBetter_IncreaseIsBetter()
    {
        var delta = AssuranceComparison.CompareMetric("evidence_traceability_f1", "Evidence Traceability F1", "higher_is_better", 0.81, 0.96);
        Assert.Equal("better", delta.Trend);
    }

    [Fact]
    public void CompareMetric_NoChange_IsUnchanged()
    {
        var delta = AssuranceComparison.CompareMetric("eghr_rate", "EGHR", "lower_is_better", 0.05, 0.05);
        Assert.Equal("unchanged", delta.Trend);
        Assert.Equal(0.0, delta.Change);
    }

    [Fact]
    public void CompareMetric_MissingValueEitherSide_HasNullChangeAndUnknownTrend()
    {
        var missingBaseline = AssuranceComparison.CompareMetric("consistency", "Consistency", "higher_is_better", null, 0.90);
        var missingCandidate = AssuranceComparison.CompareMetric("consistency", "Consistency", "higher_is_better", 0.90, null);
        Assert.Null(missingBaseline.Change);
        Assert.Equal("unknown", missingBaseline.Trend);
        Assert.Null(missingCandidate.Change);
        Assert.Equal("unknown", missingCandidate.Trend);
    }
}
