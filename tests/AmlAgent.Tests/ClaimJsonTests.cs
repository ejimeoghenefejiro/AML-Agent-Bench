using System.Text.Json.Nodes;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.ClaimJson (fix #7) -- the round-trip
/// between Claim/ReferenceEvidence and the "material_claims" JSON shape
/// judge_report.json carries, so a claim written by JudgeAgent.cs can be
/// read back by AssuranceProfileBuilder without either side re-deriving the
/// shape independently.
/// </summary>
public class ClaimJsonTests
{
    [Fact]
    public void RoundTrip_FullClaimWithAllReferenceEvidenceFields_PreservesEverything()
    {
        var original = new Claim(
            ClaimId: "MC1",
            Text: "N100 is the victim.",
            Material: true,
            AgentEvidence: new List<string> { "T1-001", "T1-002" },
            ReferenceEvidence: new ReferenceEvidence(
                Required: new List<string> { "T1-001", "T1-002" },
                AcceptableAlternatives: new List<IReadOnlyList<string>> { new List<string> { "R1", "R2" } },
                Corroborating: new List<string> { "WATCHLIST1" }));

        var json = ClaimJson.ToJsonArray(new[] { original });
        var parsed = ClaimJson.ParseArray(json);

        Assert.Single(parsed);
        var roundTripped = parsed[0];
        Assert.Equal(original.ClaimId, roundTripped.ClaimId);
        Assert.Equal(original.Text, roundTripped.Text);
        Assert.Equal(original.Material, roundTripped.Material);
        Assert.Equal(original.AgentEvidence, roundTripped.AgentEvidence);
        Assert.NotNull(roundTripped.ReferenceEvidence);
        Assert.Equal(original.ReferenceEvidence!.Required, roundTripped.ReferenceEvidence!.Required);
        Assert.Equal(original.ReferenceEvidence.AcceptableAlternatives![0], roundTripped.ReferenceEvidence.AcceptableAlternatives![0]);
        Assert.Equal(original.ReferenceEvidence.Corroborating, roundTripped.ReferenceEvidence.Corroborating);
    }

    [Fact]
    public void RoundTrip_ClaimWithNoReferenceEvidence_StaysNull()
    {
        var original = new Claim("MC2", "unannotated claim", true, new List<string> { "T1-001" }, ReferenceEvidence: null);

        var parsed = ClaimJson.ParseArray(ClaimJson.ToJsonArray(new[] { original }));

        Assert.Null(parsed[0].ReferenceEvidence);
    }

    [Fact]
    public void RoundTrip_EmptyAgentEvidence_ProducesEmptyListNotNull()
    {
        var original = new Claim("MC3", "unsupported claim", true, Array.Empty<string>(),
            new ReferenceEvidence(new List<string> { "T1-001" }));

        var parsed = ClaimJson.ParseArray(ClaimJson.ToJsonArray(new[] { original }));

        Assert.Empty(parsed[0].AgentEvidence);
        Assert.False(ClaimLevelScoring.IsSupported(parsed[0])); // required not met -- proves the round trip feeds real scoring correctly
    }

    [Fact]
    public void ParseArray_NullInput_ReturnsEmptyList()
    {
        Assert.Empty(ClaimJson.ParseArray(null));
    }

    [Fact]
    public void ParseArray_MalformedEntries_AreSkippedNotThrown()
    {
        var array = new JsonArray(
            (JsonNode)"not an object",
            (JsonNode)new JsonObject { ["claim_id"] = "MC1", ["text"] = "ok claim" });

        var parsed = ClaimJson.ParseArray(array);

        Assert.Single(parsed);
        Assert.Equal("MC1", parsed[0].ClaimId);
    }

    [Fact]
    public void ParseArray_MissingMaterialField_DefaultsTrue()
    {
        var array = new JsonArray((JsonNode)new JsonObject { ["claim_id"] = "MC1", ["text"] = "x" });
        var parsed = ClaimJson.ParseArray(array);
        Assert.True(parsed[0].Material);
    }

    [Fact]
    public void RoundTrip_MultipleAcceptableAlternatives_PreservesEachSetIndependently()
    {
        var original = new Claim("MC4", "x", true, new List<string> { "R6" },
            new ReferenceEvidence(
                Required: new List<string> { "WATCHLIST1" },
                AcceptableAlternatives: new List<IReadOnlyList<string>>
                {
                    new List<string> { "R6" },
                    new List<string> { "R6", "WATCHLIST1" },
                }));

        var parsed = ClaimJson.ParseArray(ClaimJson.ToJsonArray(new[] { original }));

        Assert.Equal(2, parsed[0].ReferenceEvidence!.AcceptableAlternatives!.Count);
        Assert.True(ClaimLevelScoring.IsSupported(parsed[0])); // matches the first alternative set
    }
}
