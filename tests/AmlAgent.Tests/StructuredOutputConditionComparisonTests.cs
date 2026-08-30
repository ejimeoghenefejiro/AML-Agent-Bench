using System.Text.Json.Nodes;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.StructuredOutputConditionComparison
/// (v0.3 validation-priorities item 4) -- comparing Condition A (free
/// narrative, LLM-mapped) against Condition B (structured citation output)
/// judge_report.json documents.
/// </summary>
public class StructuredOutputConditionComparisonTests
{
    private static JsonObject Report(string method, int citedDistinct, int fabricatedCount, double? csc, double? claimPrecision, double? claimRecall, int claimCount)
    {
        var fabricated = new JsonArray();
        for (int i = 0; i < fabricatedCount; i++) fabricated.Add((JsonNode)$"FAKE{i}");

        var claims = new JsonArray();
        for (int i = 0; i < claimCount; i++) claims.Add((JsonNode)new JsonObject { ["claim_id"] = $"MC{i}" });

        return new JsonObject
        {
            ["evidence_extraction_method"] = method,
            ["evidence_traceability"] = new JsonObject
            {
                ["cited_txn_ids_distinct"] = citedDistinct,
                ["fabricated_citations"] = fabricated,
            },
            ["claim_support_coverage"] = csc,
            ["claim_level_precision"] = claimPrecision,
            ["claim_level_recall"] = claimRecall,
            ["material_claims"] = claims,
        };
    }

    [Fact]
    public void Compare_StructuredOutputHigherOnEveryMetric_DeltasArePositive()
    {
        var a = Report("llm_mapped_from_narrative", citedDistinct: 5, fabricatedCount: 1, csc: 0.6667, claimPrecision: 0.8, claimRecall: 0.7, claimCount: 6);
        var b = Report("structured_output", citedDistinct: 6, fabricatedCount: 0, csc: 1.0, claimPrecision: 1.0, claimRecall: 1.0, claimCount: 6);

        var result = StructuredOutputConditionComparison.Compare(a, b);

        Assert.Equal(0.8, result.ReferenceValidityRateA);
        Assert.Equal(1.0, result.ReferenceValidityRateB);
        Assert.True(result.ReferenceValidityRateDelta > 0);
        Assert.True(result.ClaimSupportCoverageDelta > 0);
        Assert.True(result.ClaimLevelPrecisionDelta > 0);
        Assert.True(result.ClaimLevelRecallDelta > 0);
    }

    [Fact]
    public void Compare_IdenticalMetrics_DeltasAreZero()
    {
        var a = Report("llm_mapped_from_narrative", 4, 0, 1.0, 1.0, 1.0, 6);
        var b = Report("structured_output", 4, 0, 1.0, 1.0, 1.0, 6);

        var result = StructuredOutputConditionComparison.Compare(a, b);

        Assert.Equal(0.0, result.ReferenceValidityRateDelta);
        Assert.Equal(0.0, result.ClaimSupportCoverageDelta);
    }

    [Fact]
    public void Compare_WrongMethodOnConditionAReport_Throws()
    {
        var a = Report("structured_output", 4, 0, 1.0, 1.0, 1.0, 6); // wrong -- this is B's method
        var b = Report("structured_output", 4, 0, 1.0, 1.0, 1.0, 6);

        var ex = Assert.Throws<ArgumentException>(() => StructuredOutputConditionComparison.Compare(a, b));
        Assert.Contains("Condition A", ex.Message);
    }

    [Fact]
    public void Compare_WrongMethodOnConditionBReport_Throws()
    {
        var a = Report("llm_mapped_from_narrative", 4, 0, 1.0, 1.0, 1.0, 6);
        var b = Report("llm_mapped_from_narrative", 4, 0, 1.0, 1.0, 1.0, 6); // wrong -- this is A's method

        var ex = Assert.Throws<ArgumentException>(() => StructuredOutputConditionComparison.Compare(a, b));
        Assert.Contains("Condition B", ex.Message);
    }

    [Fact]
    public void Compare_MissingMaterialClaimsField_CountsAsZeroNotError()
    {
        var a = new JsonObject { ["evidence_extraction_method"] = "llm_mapped_from_narrative" };
        var b = new JsonObject { ["evidence_extraction_method"] = "structured_output" };

        var result = StructuredOutputConditionComparison.Compare(a, b);

        Assert.Equal(0, result.MaterialClaimCountA);
        Assert.Equal(0, result.MaterialClaimCountB);
        Assert.Null(result.ClaimSupportCoverageA);
    }

    [Fact]
    public void Compare_MissingConditionOnOneSide_DeltaIsNullNotZero()
    {
        var a = Report("llm_mapped_from_narrative", 4, 0, null, 1.0, 1.0, 6); // no CSC on A side
        var b = Report("structured_output", 4, 0, 1.0, 1.0, 1.0, 6);

        var result = StructuredOutputConditionComparison.Compare(a, b);

        Assert.Null(result.ClaimSupportCoverageDelta); // not measured on one side -- undefined, not 0
    }
}
