using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.EvidenceScoring — the pure logic behind
/// evidence traceability (the PhD's primary metric) and the legacy EGHR
/// claim-support check. Unlike the SkippableFact tests elsewhere in this
/// project, these need no workspace, no LLM call and no OPENAI_API_KEY: they
/// always run and are the fastest way to check the metric arithmetic is
/// correct.
/// </summary>
public class EvidenceScoringTests
{
    private const string SampleCsv =
        "txn_id,timestamp,source_account,destination_account,amount,source_country,destination_country,sar_linked\n" +
        "T1-001,2026-01-05T09:00:00,N001,N002,4500,GB,GB,0\n" +
        "T2-003,2026-01-12T09:30:00,X002,X003,24500,GB,GB,1\n" +
        "T3-001,2026-01-19T10:00:00,X003,EXT001,75000,GB,AE,1\n";

    [Fact]
    public void ParseTxnIdsFromCsv_ExtractsAllIds()
    {
        var ids = EvidenceScoring.ParseTxnIdsFromCsv(SampleCsv);
        Assert.Equal(new HashSet<string> { "T1-001", "T2-003", "T3-001" }, ids);
    }

    [Fact]
    public void ExtractCitedTxnIds_FindsAllOccurrencesIncludingDuplicates()
    {
        var text = "Account X003 received funds (see T2-003) and later moved them out via T3-001. T2-003 again.";
        var cited = EvidenceScoring.ExtractCitedTxnIds(text);
        Assert.Equal(3, cited.Count);
        Assert.Equal(2, cited.Count(id => id == "T2-003"));
    }

    [Fact]
    public void ComputeTraceability_FabricatedCitationIsExcludedFromGroundedSet()
    {
        var valid = new HashSet<string>(new[] { "T1-001", "T2-003", "T3-001" }, StringComparer.OrdinalIgnoreCase);
        var gold = new HashSet<string>(new[] { "T2-003", "T3-001" }, StringComparer.OrdinalIgnoreCase);
        var report = "Funds moved via T2-003 and T3-001, and also T2-999 (invented).";

        var result = EvidenceScoring.ComputeTraceability(report, valid, gold);

        Assert.Equal(3, result.CitedDistinct);
        Assert.Equal(2, result.GroundedDistinct);
        Assert.Single(result.FabricatedCitations);
        Assert.Equal("T2-999", result.FabricatedCitations[0]);
    }

    [Fact]
    public void ComputeTraceability_PrecisionAndRecallAreCorrect()
    {
        var valid = new HashSet<string>(new[] { "T1-001", "T2-003", "T3-001" }, StringComparer.OrdinalIgnoreCase);
        // Gold set of 2; report cites 1 of them plus 1 irrelevant-but-real transaction.
        var gold = new HashSet<string>(new[] { "T2-003", "T3-001" }, StringComparer.OrdinalIgnoreCase);
        var report = "See T1-001 and T2-003.";

        var result = EvidenceScoring.ComputeTraceability(report, valid, gold);

        Assert.Equal(2, result.GroundedDistinct);   // T1-001, T2-003
        Assert.Equal(1, result.MatchedGoldCitations); // only T2-003 is gold
        Assert.Equal(0.5, result.Precision);          // 1 of 2 grounded citations is gold
        Assert.Equal(0.5, result.Recall);             // 1 of 2 gold items was cited
    }

    [Fact]
    public void ComputeTraceability_NoGoldSetReturnsNullMetrics()
    {
        var valid = new HashSet<string>(new[] { "T1-001" }, StringComparer.OrdinalIgnoreCase);
        var result = EvidenceScoring.ComputeTraceability("See T1-001.", valid, goldTxnIds: null);
        Assert.Null(result.Precision);
        Assert.Null(result.Recall);
        Assert.Null(result.F1);
    }

    [Fact]
    public void ScoreClaims_FabricatedCitationForcesUnsupportedRegardlessOfLlmLabel()
    {
        var valid = new HashSet<string>(new[] { "T1-001" }, StringComparer.OrdinalIgnoreCase);
        var claims = new[]
        {
            new ClaimInput("Account X received funds from T9-999.", new[] { "T9-999" }, "supported"),
        };

        var result = EvidenceScoring.ScoreClaims(claims, valid);

        Assert.Equal("unsupported", result.Claims[0].Support);
        Assert.True(result.Claims[0].FabricatedCitation);
        Assert.Equal(1, result.UnsupportedCount);
        Assert.Equal(0, result.SupportedCount);
    }

    [Fact]
    public void ScoreClaims_RateIsUnsupportedPlusContradictedOverTotal()
    {
        var valid = new HashSet<string>(new[] { "T1-001", "T2-003" }, StringComparer.OrdinalIgnoreCase);
        var claims = new[]
        {
            new ClaimInput("supported claim", new[] { "T1-001" }, "supported"),
            new ClaimInput("contradicted claim", new[] { "T2-003" }, "contradicted"),
            new ClaimInput("unsupported claim", Array.Empty<string>(), "unsupported"),
            new ClaimInput("another supported claim", new[] { "T2-003" }, "supported"),
        };

        var result = EvidenceScoring.ScoreClaims(claims, valid);

        Assert.Equal(4, result.TotalClaims);
        Assert.Equal(2, result.SupportedCount);
        Assert.Equal(1, result.ContradictedCount);
        Assert.Equal(1, result.UnsupportedCount);
        Assert.Equal(0.5, result.Rate); // (1 unsupported + 1 contradicted) / 4
    }

    [Fact]
    public void ScoreClaims_UnknownSupportLabelDefaultsToUnsupported()
    {
        var valid = new HashSet<string>(new[] { "T1-001" }, StringComparer.OrdinalIgnoreCase);
        var claims = new[] { new ClaimInput("ambiguous claim", new[] { "T1-001" }, "maybe") };

        var result = EvidenceScoring.ScoreClaims(claims, valid);

        Assert.Equal("unsupported", result.Claims[0].Support);
        Assert.False(result.Claims[0].FabricatedCitation);
    }

    [Fact]
    public void ScoreClaims_EmptyClaimListYieldsZeroRate()
    {
        var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = EvidenceScoring.ScoreClaims(Array.Empty<ClaimInput>(), valid);
        Assert.Equal(0, result.TotalClaims);
        Assert.Equal(0.0, result.Rate);
    }

    private const string SampleJsonArray =
        """[{"txn_id":"T1-001","amount":4500},{"txn_id":"T2-003","amount":24500},{"txn_id":"T3-001","amount":75000}]""";

    [Fact]
    public void ParseTxnIdsFromJson_TopLevelArray_ExtractsAllIds()
    {
        var ids = EvidenceScoring.ParseTxnIdsFromJson(SampleJsonArray);
        Assert.Equal(new HashSet<string> { "T1-001", "T2-003", "T3-001" }, ids);
    }

    [Theory]
    [InlineData("transactions")]
    [InlineData("rows")]
    [InlineData("data")]
    [InlineData("transfers")]
    [InlineData("records")]
    public void ParseTxnIdsFromJson_WrappedUnderCommonKey_ExtractsAllIds(string wrapperKey)
    {
        var json = $$"""{"{{wrapperKey}}": [{"txn_id":"T1-001"},{"txn_id":"T2-003"}]}""";
        var ids = EvidenceScoring.ParseTxnIdsFromJson(json);
        Assert.Equal(new HashSet<string> { "T1-001", "T2-003" }, ids);
    }

    [Fact]
    public void ParseTxnIdsFromJson_CustomIdField_IsRespected()
    {
        var json = """[{"id":"T1-001"},{"id":"T2-003"}]""";
        var ids = EvidenceScoring.ParseTxnIdsFromJson(json, idField: "id");
        Assert.Equal(new HashSet<string> { "T1-001", "T2-003" }, ids);
    }

    [Fact]
    public void ParseTxnIdsFromJson_MalformedJson_ReturnsEmptySetNotThrow()
    {
        var ids = EvidenceScoring.ParseTxnIdsFromJson("{not valid json");
        Assert.Empty(ids);
    }

    [Fact]
    public void ParseTxnIdsFromJson_UnrecognisedShape_ReturnsEmptySet()
    {
        var ids = EvidenceScoring.ParseTxnIdsFromJson("""{"unexpected_key": [1,2,3]}""");
        Assert.Empty(ids);
    }

    [Fact]
    public void ParseTxnIdsFromJson_EmptyOrNullContent_ReturnsEmptySet()
    {
        Assert.Empty(EvidenceScoring.ParseTxnIdsFromJson(""));
        Assert.Empty(EvidenceScoring.ParseTxnIdsFromJson("   "));
    }

    [Fact]
    public void ParseTxnIdsFromFile_DispatchesByExtension()
    {
        Assert.Equal(new HashSet<string> { "T1-001", "T2-003", "T3-001" },
            EvidenceScoring.ParseTxnIdsFromFile(SampleCsv, "data/weekly_transfers.csv"));
        Assert.Equal(new HashSet<string> { "T1-001", "T2-003", "T3-001" },
            EvidenceScoring.ParseTxnIdsFromFile(SampleJsonArray, "data/weekly_transfers.json"));
    }

    [Fact]
    public void ParseTxnIdsFromFile_UnsupportedExtension_ReturnsEmptySetNotThrow()
    {
        var ids = EvidenceScoring.ParseTxnIdsFromFile("whatever content", "data/weekly_transfers.xlsx");
        Assert.Empty(ids);
    }

    [Fact]
    public void ParseTxnIdsFromFile_CsvAndJsonOfSameData_ProduceIdenticalSets()
    {
        var fromCsv = EvidenceScoring.ParseTxnIdsFromFile(SampleCsv, "a.csv");
        var fromJson = EvidenceScoring.ParseTxnIdsFromFile(SampleJsonArray, "a.json");
        Assert.Equal(fromCsv, fromJson);
    }
}
