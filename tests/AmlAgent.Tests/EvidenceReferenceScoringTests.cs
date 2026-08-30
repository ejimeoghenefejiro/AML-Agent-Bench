using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Tests for the generalised evidence-traceability API (EvidenceReference,
/// EvidenceScoring.ExtractCitedEvidenceIds, and the
/// ComputeTraceability(string, IReadOnlyCollection&lt;EvidenceReference&gt;, ...)
/// overload) -- the fix for evidence traceability being hardcoded to
/// transaction-ID-shaped citations only. See
/// docs/evidence-traceability-framework.md#traceability-failure-taxonomy for
/// the gap this closes, and EvidenceReferenceCrossSourceRegressionTests for
/// the concrete previously-broken scenario now working.
/// </summary>
public class EvidenceReferenceScoringTests
{
    private static EvidenceReference Txn(string id) => new(id, "transaction", "csv");
    private static EvidenceReference Rel(string id) => new(id, "relationship", "graphml");
    private static EvidenceReference Sar(string id) => new(id, "sar", "json");
    private static EvidenceReference Watchlist(string id) => new(id, "watchlist", "json");

    // -- ExtractCitedEvidenceIds --

    [Fact]
    public void ExtractCitedEvidenceIds_RecognisesNonTransactionShapedIds()
    {
        var known = new[] { Rel("R1"), Sar("SAR-2026-001"), Watchlist("WATCHLIST1") };
        var cited = EvidenceScoring.ExtractCitedEvidenceIds(
            "The relationship R1 is corroborated by SAR-2026-001 and a prior WATCHLIST1 flag.", known);

        Assert.Contains("R1", cited);
        Assert.Contains("SAR-2026-001", cited);
        Assert.Contains("WATCHLIST1", cited);
    }

    [Fact]
    public void ExtractCitedEvidenceIds_DoesNotMatchIdAsSubstringOfALongerToken()
    {
        var known = new[] { Rel("R1") };
        var cited = EvidenceScoring.ExtractCitedEvidenceIds("See relationship R100 for context.", known);
        Assert.DoesNotContain("R1", cited);
    }

    [Fact]
    public void ExtractCitedEvidenceIds_UnknownTokenNeverExtracted()
    {
        var known = new[] { Rel("R1") };
        var cited = EvidenceScoring.ExtractCitedEvidenceIds("R2 is not mentioned as evidence.", known);
        Assert.DoesNotContain("R2", cited);
    }

    [Fact]
    public void ExtractCitedEvidenceIds_RepeatedMention_CountedEachTime()
    {
        var known = new[] { Rel("R1") };
        var cited = EvidenceScoring.ExtractCitedEvidenceIds("R1 shows this. R1 confirms it. Again, R1.", known);
        Assert.Equal(3, cited.Count(c => c == "R1"));
    }

    [Fact]
    public void ExtractCitedEvidenceIds_EmptyKnownEvidence_ReturnsEmpty()
    {
        var cited = EvidenceScoring.ExtractCitedEvidenceIds("R1 mentioned here.", Array.Empty<EvidenceReference>());
        Assert.Empty(cited);
    }

    [Fact]
    public void ExtractCitedEvidenceIds_CaseInsensitiveMatch_ReturnsCanonicalCasing()
    {
        var known = new[] { Rel("R1") };
        var cited = EvidenceScoring.ExtractCitedEvidenceIds("see r1 for details", known);
        Assert.Contains("R1", cited); // canonical casing from the reference, not the text's "r1"
    }

    // -- Generalised ComputeTraceability --

    [Fact]
    public void ComputeTraceability_CrossSourceEvidence_AllTypesGroundedCorrectly()
    {
        var valid = new[] { Txn("T1-001"), Rel("R1"), Watchlist("WATCHLIST1") };
        var gold = new[] { Txn("T1-001"), Rel("R1"), Watchlist("WATCHLIST1") };

        var result = EvidenceScoring.ComputeTraceability(
            "N100 sent funds to M201 (T1-001), an edge confirmed in the relationship graph (R1), corroborated by a watchlist flag (WATCHLIST1).",
            valid, gold);

        Assert.Equal(3, result.GroundedDistinct);
        Assert.Equal(1.0, result.Precision);
        Assert.Equal(1.0, result.Recall);
        Assert.Equal(1.0, result.F1);
    }

    [Fact]
    public void ComputeTraceability_FabricatedTransactionShapedId_StillCaughtAsFabricated()
    {
        var valid = new[] { Rel("R1") };
        var result = EvidenceScoring.ComputeTraceability("R1, corroborated by T3-999.", valid, valid);

        Assert.Contains("T3-999", result.FabricatedCitations);
    }

    [Fact]
    public void ComputeTraceability_RealTransactionAndRealRelationship_NoDoubleCountingInCitedTotal()
    {
        // T1-001 is found by BOTH the known-id pass and the legacy txn-shape
        // regex -- must not be counted twice for one literal occurrence.
        var valid = new[] { Txn("T1-001"), Rel("R1") };
        var result = EvidenceScoring.ComputeTraceability("T1-001 and R1 both apply.", valid, valid);

        Assert.Equal(2, result.CitedTotal);
        Assert.Equal(2, result.CitedDistinct);
    }

    [Fact]
    public void ComputeTraceability_MissingGoldEvidence_ReportedRegardlessOfType()
    {
        var valid = new[] { Txn("T1-001"), Sar("SAR1") };
        var gold = new[] { Txn("T1-001"), Sar("SAR1") };

        var result = EvidenceScoring.ComputeTraceability("Only T1-001 is cited.", valid, gold);

        Assert.Contains("SAR1", result.MissingGoldCitationsList);
    }

    [Fact]
    public void ComputeTraceability_EmptyValidEvidence_HandlesGracefully()
    {
        var result = EvidenceScoring.ComputeTraceability("R1 mentioned.", Array.Empty<EvidenceReference>(), null);
        Assert.Equal(0, result.GroundedDistinct);
        Assert.Null(result.Precision);
    }

    [Fact]
    public void ComputeTraceability_NullGoldEvidence_PrecisionRecallNull()
    {
        var valid = new[] { Rel("R1") };
        var result = EvidenceScoring.ComputeTraceability("R1 cited.", valid, null);
        Assert.Null(result.Precision);
        Assert.Null(result.Recall);
    }

    [Fact]
    public void ComputeTraceability_IsDeterministic()
    {
        var valid = new[] { Txn("T1-001"), Rel("R1") };
        var r1 = EvidenceScoring.ComputeTraceability("T1-001 and R1.", valid, valid);
        var r2 = EvidenceScoring.ComputeTraceability("T1-001 and R1.", valid, valid);

        Assert.Equal(r1.Precision, r2.Precision);
        Assert.Equal(r1.Recall, r2.Recall);
        Assert.Equal(r1.CitedTotal, r2.CitedTotal);
        Assert.Equal(r1.GroundedCitations, r2.GroundedCitations);
    }

    // -- Fix #3 (v0.3): generic fabricated-evidence detection beyond transaction shapes --

    [Fact]
    public void InferEvidenceIdShapes_GeneralisesFromRealIdsWithDigits()
    {
        var known = new[] { Rel("R1"), Rel("R2"), Watchlist("WATCHLIST1"), Sar("SAR-2026-001") };
        var shapes = EvidenceScoring.InferEvidenceIdShapes(known);

        Assert.Contains(shapes, s => s.IsMatch("R99"));
        Assert.Contains(shapes, s => s.IsMatch("WATCHLIST7"));
        Assert.Contains(shapes, s => s.IsMatch("SAR-2027-042"));
    }

    [Fact]
    public void InferEvidenceIdShapes_IdWithNoDigits_ProducesNoShape()
    {
        var known = new[] { Rel("VICTIM") };
        var shapes = EvidenceScoring.InferEvidenceIdShapes(known);
        Assert.DoesNotContain(shapes, s => s.IsMatch("VICTIM2")); // no digit run to generalise from
    }

    [Fact]
    public void ExtractShapeFabricatedIds_FabricatedRelationshipId_Recognised()
    {
        // R99 was never real, but the case has real R1..R7 -- the shape "R\d+"
        // is inferred from those and R99 matches it, so it's caught as a
        // plausible fabrication instead of being silently invisible.
        var known = new[] { Rel("R1"), Rel("R2"), Rel("R7") };
        var fabricated = EvidenceScoring.ExtractShapeFabricatedIds("Corroborated by relationship R99.", known);
        Assert.Contains("R99", fabricated);
    }

    [Fact]
    public void ExtractShapeFabricatedIds_FabricatedWatchlistId_Recognised()
    {
        var known = new[] { Watchlist("WATCHLIST1") };
        var fabricated = EvidenceScoring.ExtractShapeFabricatedIds("Flagged per WATCHLIST9.", known);
        Assert.Contains("WATCHLIST9", fabricated);
    }

    [Fact]
    public void ExtractShapeFabricatedIds_RealId_NotFlaggedAsFabricated()
    {
        var known = new[] { Rel("R1") };
        var fabricated = EvidenceScoring.ExtractShapeFabricatedIds("See R1.", known);
        Assert.DoesNotContain("R1", fabricated);
    }

    [Fact]
    public void ExtractShapeFabricatedIds_TransactionShapedToken_ExcludedToAvoidDoubleCounting()
    {
        // A transaction-shaped fabrication is the legacy regex's job (fix #1);
        // this method must not also report it, or ComputeTraceability would
        // count one text occurrence twice.
        var known = new[] { Rel("R1") };
        var fabricated = EvidenceScoring.ExtractShapeFabricatedIds("See T3-999.", known);
        Assert.DoesNotContain("T3-999", fabricated);
    }

    [Fact]
    public void ComputeTraceability_FabricatedRelationshipId_NowCaughtAsFabricated()
    {
        // The gap fix #1's own doc comment used to flag as "still open":
        // a fabricated relationship id is now caught, not just fabricated
        // transaction ids.
        var valid = new[] { Rel("R1"), Rel("R2"), Txn("T1-001") };
        var gold = new[] { Rel("R1") };
        var result = EvidenceScoring.ComputeTraceability("R1 confirms this, corroborated by R99.", valid, gold);

        Assert.Contains("R99", result.FabricatedCitations);
        Assert.Equal(1, result.GroundedDistinct); // only R1
    }

    [Fact]
    public void ComputeTraceability_RealAndFabricatedOfSameType_NoDoubleCountingInCitedTotal()
    {
        var valid = new[] { Rel("R1"), Rel("R2") };
        var result = EvidenceScoring.ComputeTraceability("R1 is real, R99 is not.", valid, valid);

        // One mention each -> CitedTotal must be exactly 2, not inflated by
        // any overlap between the known-id pass and the shape-fabrication pass.
        Assert.Equal(2, result.CitedTotal);
        Assert.Equal(2, result.CitedDistinct);
    }

    [Fact]
    public void ComputeTraceability_NoRealExamplesOfAType_CannotInferShapeForThatType()
    {
        // Honest limitation, not a bug: with no real SAR ids anywhere in
        // validEvidence, there's nothing to generalise a SAR shape from, so a
        // fabricated-looking SAR id is invisible -- exactly like any other
        // evidence type with zero real examples in the case.
        var valid = new[] { Rel("R1") };
        var result = EvidenceScoring.ComputeTraceability("See SAR-2099-999.", valid, valid);
        Assert.DoesNotContain("SAR-2099-999", result.FabricatedCitations);
    }

    [Fact]
    public void ComputeTraceability_MatchesLegacyOverload_ForTransactionOnlyInputs()
    {
        // Same logical inputs through both overloads must produce identical results.
        var validRefs = new[] { Txn("T1-001"), Txn("T1-002") };
        var goldRefs = new[] { Txn("T1-001") };
        const string text = "T1-001 confirms this, T1-099 does not exist.";

        var viaGeneralised = EvidenceScoring.ComputeTraceability(text, validRefs, goldRefs);
        var viaLegacy = EvidenceScoring.ComputeTraceability(
            text,
            new HashSet<string>(validRefs.Select(r => r.EvidenceId), StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(goldRefs.Select(r => r.EvidenceId), StringComparer.OrdinalIgnoreCase));

        Assert.Equal(viaLegacy.Precision, viaGeneralised.Precision);
        Assert.Equal(viaLegacy.Recall, viaGeneralised.Recall);
        Assert.Equal(viaLegacy.CitedDistinct, viaGeneralised.CitedDistinct);
        Assert.Equal(viaLegacy.FabricatedCitations, viaGeneralised.FabricatedCitations);
    }
}
