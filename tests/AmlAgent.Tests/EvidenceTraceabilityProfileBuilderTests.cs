using System.Text.Json.Nodes;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.EvidenceTraceabilityProfileBuilder --
/// the additive, backward-compatible Evidence Traceability Profile schema
/// (see docs/evidence-traceability-framework.md). Pure JSON transformation
/// over the existing judge_report.json "eghr"/"evidence_traceability" shapes,
/// so these are fast, always-on tests with no workspace or LLM dependency.
/// </summary>
public class EvidenceTraceabilityProfileBuilderTests
{
    private static JsonObject Eghr(int total, int supported, int unsupported, int contradicted, JsonArray? claims = null) => new()
    {
        ["total_claims"] = total,
        ["supported_count"] = supported,
        ["unsupported_count"] = unsupported,
        ["contradicted_count"] = contradicted,
        ["rate"] = total == 0 ? 0.0 : Math.Round((double)(unsupported + contradicted) / total, 4),
        ["claims"] = claims ?? new JsonArray(),
    };

    private static JsonObject Trace(int citedDistinct, JsonArray fabricated, JsonArray missingGold, double? precision, double? recall, double? f1) => new()
    {
        ["cited_txn_ids_distinct"] = citedDistinct,
        ["fabricated_citations"] = fabricated,
        ["missing_gold_citations_list"] = missingGold,
        ["precision"] = precision,
        ["recall"] = recall,
        ["f1"] = f1,
    };

    [Fact]
    public void Build_ValidCitationNoFabrication_ReferenceValidityRateIsOne()
    {
        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(2, 2, 0, 0),
            Trace(2, new JsonArray(), new JsonArray(), 1.0, 1.0, 1.0));

        Assert.Equal(1.0, (double?)profile["reference_validity_rate"]);
        Assert.Empty(profile["traceability_failures"]!.AsArray());
    }

    [Fact]
    public void Build_InvalidNonexistentIds_ReferenceValidityRateReflectsFabrication()
    {
        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(2, 1, 1, 0),
            Trace(4, new JsonArray("T9-999"), new JsonArray(), 0.75, 1.0, 0.8571));

        // 3 of 4 distinct citations are real -> 0.75
        Assert.Equal(0.75, (double?)profile["reference_validity_rate"]);
        Assert.Equal(1, (int)profile["invalid_reference_count"]!);
    }

    [Fact]
    public void Build_EmptyCitationSet_ReferenceValidityRateIsNullNotZero()
    {
        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(0, 0, 0, 0),
            Trace(0, new JsonArray(), new JsonArray(), null, null, null));

        Assert.Null(profile["reference_validity_rate"]);
    }

    [Fact]
    public void Build_DuplicateFabricatedCitations_CountedOnceEachAsInvalidReference()
    {
        // Deduping happens upstream (EvidenceScoring); this builder just reflects
        // whatever distinct fabricated list it's given -- pin that contract down.
        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(1, 0, 1, 0),
            Trace(3, new JsonArray("T9-001", "T9-002"), new JsonArray(), 0.3333, 1.0, 0.5));

        Assert.Equal(2, (int)profile["invalid_reference_count"]!);
        var invalidRefFailures = profile["traceability_failures"]!.AsArray()
            .Where(f => (string?)f!["failure_type"] == "invalid_reference").ToList();
        Assert.Equal(2, invalidRefFailures.Count);
    }

    [Fact]
    public void Build_IrrelevantCitations_PrecisionPassedThroughUnmodified()
    {
        // The builder does not recompute precision/recall/F1 -- it reports
        // exactly what EvidenceScoring.ComputeTraceability already produced.
        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(1, 1, 0, 0),
            Trace(3, new JsonArray(), new JsonArray(), 0.3333, 1.0, 0.5));

        Assert.Equal(0.3333, (double?)profile["evidence_precision"]);
    }

    [Fact]
    public void Build_OmittedGoldEvidence_RecordedAsEvidenceOmissionFailures()
    {
        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(1, 1, 0, 0),
            Trace(1, new JsonArray(), new JsonArray("T1-005", "T1-006"), 1.0, 0.3333, 0.5));

        var omissions = profile["traceability_failures"]!.AsArray()
            .Where(f => (string?)f!["failure_type"] == "evidence_omission").ToList();
        Assert.Equal(2, omissions.Count);
        Assert.Contains(omissions, f => (string?)f!["evidence_id"] == "T1-005");
    }

    [Fact]
    public void Build_F1EdgeCase_NullWhenPrecisionAndRecallBothNull()
    {
        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(0, 0, 0, 0),
            Trace(0, new JsonArray(), new JsonArray(), null, null, null));

        Assert.Null(profile["evidence_traceability_f1"]);
    }

    [Fact]
    public void Build_NoReferenceNoGold_AllCoreFieldsNullNotZero()
    {
        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(0, 0, 0, 0),
            Trace(0, new JsonArray(), new JsonArray(), null, null, null));

        Assert.Null(profile["evidence_precision"]);
        Assert.Null(profile["evidence_recall"]);
        Assert.Null(profile["evidence_traceability_f1"]);
        Assert.Null(profile["reference_validity_rate"]);
    }

    [Fact]
    public void Build_UnimplementedFields_AreExplicitlyNullNeverFabricatedZero()
    {
        var profile = EvidenceTraceabilityProfileBuilder.Build(Eghr(1, 1, 0, 0), Trace(1, new JsonArray(), new JsonArray(), 1.0, 1.0, 1.0));

        Assert.True(profile.ContainsKey("claim_support_coverage"));
        Assert.Null(profile["claim_support_coverage"]);
        Assert.True(profile.ContainsKey("evidence_sufficiency_rate"));
        Assert.Null(profile["evidence_sufficiency_rate"]);
    }

    [Fact]
    public void Build_UnsupportedClaims_RecordedInFailureTaxonomy()
    {
        var claims = new JsonArray(
            new JsonObject { ["text"] = "N100 sent funds to M201.", ["support"] = "supported" },
            new JsonObject { ["text"] = "M201 has prior convictions.", ["support"] = "unsupported" });

        var profile = EvidenceTraceabilityProfileBuilder.Build(
            Eghr(2, 1, 1, 0, claims),
            Trace(1, new JsonArray(), new JsonArray(), 1.0, 1.0, 1.0));

        var unsupported = profile["traceability_failures"]!.AsArray()
            .Where(f => (string?)f!["failure_type"] == "unsupported_claim").ToList();
        Assert.Single(unsupported);
        Assert.Equal(1, (int)profile["unsupported_claim_count"]!);
    }

    [Fact]
    public void Build_NullInputs_DoesNotThrow_AllFieldsNull()
    {
        var profile = EvidenceTraceabilityProfileBuilder.Build(null, null);

        Assert.Null(profile["evidence_precision"]);
        Assert.Null(profile["reference_validity_rate"]);
        Assert.Empty(profile["traceability_failures"]!.AsArray());
    }

    [Fact]
    public void Build_IsDeterministic_SameInputsProduceSameOutput()
    {
        var eghr = Eghr(2, 1, 1, 0);
        var trace = Trace(2, new JsonArray("T9-999"), new JsonArray("T1-005"), 0.5, 0.5, 0.5);

        var p1 = EvidenceTraceabilityProfileBuilder.Build(eghr, trace).ToJsonString();
        var p2 = EvidenceTraceabilityProfileBuilder.Build(eghr, trace).ToJsonString();

        Assert.Equal(p1, p2);
    }
}
