using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.StructuredClaimEvidenceReader (v0.3
/// validation-priorities item 4) -- the agent's optional structured
/// claim-evidence output, claim_evidence.json.
/// </summary>
public class StructuredClaimEvidenceTests
{
    private const string ValidJson = """
    {
      "schema_version": "1.0",
      "claims": [
        {
          "claim_id": "MC1",
          "text": "N100 is the victim.",
          "evidence": [
            { "evidence_id": "T1-001", "evidence_type": "transaction" },
            { "evidence_id": "T1-002", "evidence_type": "transaction" }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ValidFile_ReadsAllFields()
    {
        var set = StructuredClaimEvidenceReader.Parse(ValidJson);
        Assert.Equal("1.0", set.SchemaVersion);
        Assert.Single(set.Claims);

        var claim = set.Claims[0];
        Assert.Equal("MC1", claim.ClaimId);
        Assert.Equal("N100 is the victim.", claim.Text);
        Assert.Equal(2, claim.Evidence.Count);
        Assert.Equal("T1-001", claim.Evidence[0].EvidenceId);
        Assert.Equal("transaction", claim.Evidence[0].EvidenceType);
    }

    [Fact]
    public void Parse_MissingSchemaVersion_Throws()
    {
        const string json = """{ "claims": [] }""";
        var ex = Assert.Throws<InvalidStructuredClaimEvidenceException>(() => StructuredClaimEvidenceReader.Parse(json));
        Assert.Contains("schema_version", ex.Message);
    }

    [Fact]
    public void Parse_MissingClaims_Throws()
    {
        const string json = """{ "schema_version": "1.0" }""";
        var ex = Assert.Throws<InvalidStructuredClaimEvidenceException>(() => StructuredClaimEvidenceReader.Parse(json));
        Assert.Contains("claims", ex.Message);
    }

    [Fact]
    public void Parse_EmptyClaimsArray_IsValid_NotAnError()
    {
        // An agent that produces the file but genuinely found nothing
        // structured to report is a real (if weak) submission, not malformed.
        const string json = """{ "schema_version": "1.0", "claims": [] }""";
        var set = StructuredClaimEvidenceReader.Parse(json);
        Assert.Empty(set.Claims);
    }

    [Fact]
    public void Parse_ClaimWithEmptyEvidenceList_IsValid_MeansUnsupported()
    {
        // A listed claim with zero evidence is a meaningful signal (the
        // agent asserts this but cites nothing for it) -- not a parse error.
        const string json = """
        { "schema_version": "1.0", "claims": [
          { "claim_id": "MC1", "text": "x", "evidence": [] }
        ]}
        """;
        var set = StructuredClaimEvidenceReader.Parse(json);
        Assert.Empty(set.Claims[0].Evidence);
    }

    [Fact]
    public void Parse_ClaimMissingClaimId_Throws()
    {
        const string json = """
        { "schema_version": "1.0", "claims": [ { "text": "x", "evidence": [] } ] }
        """;
        var ex = Assert.Throws<InvalidStructuredClaimEvidenceException>(() => StructuredClaimEvidenceReader.Parse(json));
        Assert.Contains("claim_id", ex.Message);
    }

    [Fact]
    public void Parse_EvidenceMissingEvidenceId_Throws()
    {
        const string json = """
        { "schema_version": "1.0", "claims": [
          { "claim_id": "MC1", "text": "x", "evidence": [ { "evidence_type": "transaction" } ] }
        ]}
        """;
        var ex = Assert.Throws<InvalidStructuredClaimEvidenceException>(() => StructuredClaimEvidenceReader.Parse(json));
        Assert.Contains("evidence_id", ex.Message);
    }

    [Fact]
    public void Parse_EvidenceTypeIsOptional()
    {
        const string json = """
        { "schema_version": "1.0", "claims": [
          { "claim_id": "MC1", "text": "x", "evidence": [ { "evidence_id": "T1-001" } ] }
        ]}
        """;
        var set = StructuredClaimEvidenceReader.Parse(json);
        Assert.Null(set.Claims[0].Evidence[0].EvidenceType);
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        Assert.Throws<InvalidStructuredClaimEvidenceException>(() => StructuredClaimEvidenceReader.Parse("{ not json"));
    }
}
