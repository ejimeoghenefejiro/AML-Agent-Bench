using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.GoldClaimAnnotationReader (v0.3
/// validation-priorities item 1) -- the schema for one annotator's
/// independently-derived material claims, distinct from HumanAnnotation
/// (which reviews a candidate's report, not the case itself).
/// </summary>
public class GoldClaimAnnotationTests
{
    private const string ValidJson = """
    {
      "schema_version": "1.0",
      "task_id": "task-007-multi-source-mule-network",
      "annotator_id": "H01",
      "adjudication_status": "single-annotator",
      "claims": [
        {
          "claim_id": "MC1",
          "text": "N100 is the victim.",
          "required": ["T1-001", "T1-002"],
          "acceptable_alternatives": [["R1", "R2"]],
          "rationale": "Both outbound transfers establish N100 as the funds' origin."
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ValidFile_ReadsAllFields()
    {
        var set = GoldClaimAnnotationReader.Parse(ValidJson);
        Assert.Equal("1.0", set.SchemaVersion);
        Assert.Equal("task-007-multi-source-mule-network", set.TaskId);
        Assert.Equal("H01", set.AnnotatorId);
        Assert.Equal("single-annotator", set.AdjudicationStatus);
        Assert.Single(set.Claims);

        var claim = set.Claims[0];
        Assert.Equal("MC1", claim.ClaimId);
        Assert.Equal(new[] { "T1-001", "T1-002" }, claim.Required);
        Assert.Equal(new[] { "R1", "R2" }, claim.AcceptableAlternatives![0]);
        Assert.False(string.IsNullOrWhiteSpace(claim.Rationale));
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("task_id")]
    [InlineData("annotator_id")]
    [InlineData("adjudication_status")]
    public void Parse_MissingRequiredTopLevelField_Throws(string field)
    {
        var json = ValidJson.Replace($"\"{field}\"", "\"_removed_\"");
        var ex = Assert.Throws<InvalidGoldClaimAnnotationException>(() => GoldClaimAnnotationReader.Parse(json));
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void Parse_InvalidAdjudicationStatus_Throws()
    {
        var json = ValidJson.Replace("single-annotator", "not-a-real-status");
        var ex = Assert.Throws<InvalidGoldClaimAnnotationException>(() => GoldClaimAnnotationReader.Parse(json));
        Assert.Contains("not-a-real-status", ex.Message);
    }

    [Fact]
    public void Parse_EmptyClaims_Throws()
    {
        const string json = """{ "schema_version":"1.0", "task_id":"x", "annotator_id":"H01", "adjudication_status":"draft", "claims": [] }""";
        Assert.Throws<InvalidGoldClaimAnnotationException>(() => GoldClaimAnnotationReader.Parse(json));
    }

    [Fact]
    public void Parse_ClaimMissingRequired_Throws()
    {
        const string json = """
        { "schema_version":"1.0", "task_id":"x", "annotator_id":"H01", "adjudication_status":"draft",
          "claims": [ { "claim_id": "MC1", "text": "x" } ] }
        """;
        var ex = Assert.Throws<InvalidGoldClaimAnnotationException>(() => GoldClaimAnnotationReader.Parse(json));
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void Parse_ClaimWithNoOptionalFields_StillParses()
    {
        const string json = """
        { "schema_version":"1.0", "task_id":"x", "annotator_id":"H01", "adjudication_status":"draft",
          "claims": [ { "claim_id": "MC1", "text": "x", "required": [] } ] }
        """;
        var set = GoldClaimAnnotationReader.Parse(json);
        Assert.Empty(set.Claims[0].Required);
        Assert.Null(set.Claims[0].AcceptableAlternatives);
        Assert.Null(set.Claims[0].Corroborating);
        Assert.Null(set.Claims[0].Rationale);
    }
}
