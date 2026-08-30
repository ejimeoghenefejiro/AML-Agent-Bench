using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.ClaimAnnotationAdjudication (v0.3
/// validation-priorities item 1) -- comparing two independent annotators'
/// GoldClaimAnnotationSets and merging an adjudicator's resolutions. Uses
/// synthetic, clearly-labelled fixtures with a genuine disagreement built in,
/// the same discipline as validation/gold/sufficiency's fixture -- these are
/// never presented as real annotation data.
/// </summary>
public class ClaimAnnotationAdjudicationTests
{
    private static GoldClaimAnnotation Claim(string id, params string[] required) =>
        new(id, $"claim {id}", required, null, null, null);

    [Fact]
    public void Compare_IdenticalRequiredSets_MarkedAsMatching()
    {
        var a = new GoldClaimAnnotationSet("1.0", "task-007", "H01", "single-annotator",
            new[] { Claim("MC1", "T1-001", "T1-002") });
        var b = new GoldClaimAnnotationSet("1.0", "task-007", "H02", "single-annotator",
            new[] { Claim("MC1", "T1-002", "T1-001") }); // same set, different order

        var comparison = ClaimAnnotationAdjudication.Compare(a, b);

        Assert.Equal(1, comparison.ComparedClaimCount);
        Assert.Equal(1, comparison.RequiredMatchCount);
        Assert.True(comparison.PerClaim[0].RequiredMatches);
        Assert.Equal(1.0, comparison.AgreementRate);
    }

    [Fact]
    public void Compare_DifferentRequiredSets_SurfacesExactDifference()
    {
        var a = new GoldClaimAnnotationSet("1.0", "task-007", "H01", "single-annotator",
            new[] { Claim("MC3", "T1-003", "T1-004") });
        var b = new GoldClaimAnnotationSet("1.0", "task-007", "H02", "single-annotator",
            new[] { Claim("MC3", "T1-003") }); // H02 thinks only T1-003 is required, not T1-004

        var comparison = ClaimAnnotationAdjudication.Compare(a, b);

        Assert.False(comparison.PerClaim[0].RequiredMatches);
        Assert.Contains("T1-004", comparison.PerClaim[0].RequiredOnlyInFirst);
        Assert.Empty(comparison.PerClaim[0].RequiredOnlyInSecond);
        Assert.Equal(0.0, comparison.AgreementRate);
    }

    [Fact]
    public void Compare_ClaimOnlyIdentifiedByOneAnnotator_ReportedSeparately()
    {
        var a = new GoldClaimAnnotationSet("1.0", "task-007", "H01", "single-annotator",
            new[] { Claim("MC1", "T1-001"), Claim("MC7", "T2-002") }); // H01 thinks MC7 is material
        var b = new GoldClaimAnnotationSet("1.0", "task-007", "H02", "single-annotator",
            new[] { Claim("MC1", "T1-001") }); // H02 does not

        var comparison = ClaimAnnotationAdjudication.Compare(a, b);

        Assert.Equal(1, comparison.ComparedClaimCount); // only MC1 is comparable
        Assert.Contains("MC7", comparison.ClaimIdsOnlyInFirst);
        Assert.Empty(comparison.ClaimIdsOnlyInSecond);
    }

    [Fact]
    public void Adjudicate_ProducesFinalSetMarkedAdjudicated()
    {
        var resolved = new[] { Claim("MC3", "T1-003", "T1-004") }; // adjudicator sided with the annotator who included T1-004
        var final = ClaimAnnotationAdjudication.Adjudicate("task-007-multi-source-mule-network", resolved);

        Assert.Equal("adjudicated", final.AdjudicationStatus);
        Assert.Equal("task-007-multi-source-mule-network", final.TaskId);
        Assert.Single(final.Claims);
        Assert.Equal(new[] { "T1-003", "T1-004" }, final.Claims[0].Required);
    }

    [Fact]
    public void Adjudicate_EmptyResolution_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ClaimAnnotationAdjudication.Adjudicate("task-007", Array.Empty<GoldClaimAnnotation>()));
    }

    [Fact]
    public void Adjudicate_DoesNotMutateOriginalAnnotatorSets()
    {
        // Preserving pre-adjudication annotations is a stated acceptance
        // criterion -- prove Adjudicate never touches its inputs.
        var original = new GoldClaimAnnotationSet("1.0", "task-007", "H01", "single-annotator",
            new[] { Claim("MC1", "T1-001") });

        ClaimAnnotationAdjudication.Adjudicate("task-007", new[] { Claim("MC1", "T1-001", "T1-002") });

        Assert.Equal(new[] { "T1-001" }, original.Claims[0].Required); // unchanged
        Assert.Equal("single-annotator", original.AdjudicationStatus); // unchanged
    }
}
