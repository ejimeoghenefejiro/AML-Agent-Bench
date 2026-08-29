using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Tests for the claim-level model (Claim, ReferenceEvidence) and its
/// scoring (ClaimLevelScoring) -- implementing what
/// docs/evidence-traceability-framework.md described but the codebase
/// previously only computed at report level. See
/// docs/research-scope-mapping.md#planned-claim-level-schema for the schema
/// this implements.
/// </summary>
public class ClaimLevelScoringTests
{
    private static Claim MaterialClaim(string id, IReadOnlyList<string> agentEvidence, ReferenceEvidence? ReferenceEvidence) =>
        new(id, $"claim {id}", Material: true, AgentEvidence: agentEvidence, ReferenceEvidence: ReferenceEvidence);

    // -- IsSupported --

    [Fact]
    public void IsSupported_AllRequiredCited_True()
    {
        var claim = MaterialClaim("C1", new[] { "T1", "T2" }, new ReferenceEvidence(Required: new[] { "T1", "T2" }));
        Assert.True(ClaimLevelScoring.IsSupported(claim));
    }

    [Fact]
    public void IsSupported_PartialRequired_False()
    {
        var claim = MaterialClaim("C1", new[] { "T1" }, new ReferenceEvidence(Required: new[] { "T1", "T2" }));
        Assert.False(ClaimLevelScoring.IsSupported(claim));
    }

    [Fact]
    public void IsSupported_AcceptableAlternativeFullyCited_True()
    {
        // Agent used the alternative evidence set instead of Required -- still supported.
        var claim = MaterialClaim("C1", new[] { "T3", "T4" }, new ReferenceEvidence(
            Required: new[] { "T1" },
            AcceptableAlternatives: new[] { new[] { "T3", "T4" } }));

        Assert.True(ClaimLevelScoring.IsSupported(claim));
    }

    [Fact]
    public void IsSupported_PartialAlternative_NotEnoughOnItsOwn_False()
    {
        var claim = MaterialClaim("C1", new[] { "T3" }, new ReferenceEvidence(
            Required: new[] { "T1" },
            AcceptableAlternatives: new[] { new[] { "T3", "T4" } }));

        Assert.False(ClaimLevelScoring.IsSupported(claim));
    }

    [Fact]
    public void IsSupported_EmptyRequiredNoAlternatives_VacuouslyTrue()
    {
        var claim = MaterialClaim("C1", Array.Empty<string>(), new ReferenceEvidence(Required: Array.Empty<string>()));
        Assert.True(ClaimLevelScoring.IsSupported(claim));
    }

    [Fact]
    public void IsSupported_NoReferenceEvidence_VacuouslyTrue()
    {
        var claim = MaterialClaim("C1", new[] { "T1" }, ReferenceEvidence: null);
        Assert.True(ClaimLevelScoring.IsSupported(claim));
    }

    [Fact]
    public void IsSupported_CorroboratingEvidenceAlone_DoesNotSatisfyRequired()
    {
        // Corroborating never substitutes for Required.
        var claim = MaterialClaim("C1", new[] { "T5" }, new ReferenceEvidence(
            Required: new[] { "T1" },
            Corroborating: new[] { "T5" }));

        Assert.False(ClaimLevelScoring.IsSupported(claim));
    }

    // -- ResolveReferenceSet --

    [Fact]
    public void ResolveReferenceSet_RequiredAndCorroborating_BothIncluded()
    {
        var claim = MaterialClaim("C1", new[] { "T1" }, new ReferenceEvidence(
            Required: new[] { "T1" }, Corroborating: new[] { "T2" }));

        var resolved = ClaimLevelScoring.ResolveReferenceSet(claim);
        Assert.Contains("T1", resolved);
        Assert.Contains("T2", resolved);
    }

    [Fact]
    public void ResolveReferenceSet_AgentUsedAlternative_AlternativeIncludedNotPenalised()
    {
        var claim = MaterialClaim("C1", new[] { "T3", "T4" }, new ReferenceEvidence(
            Required: new[] { "T1" },
            AcceptableAlternatives: new[] { new[] { "T3", "T4" } }));

        var resolved = ClaimLevelScoring.ResolveReferenceSet(claim);
        Assert.Contains("T3", resolved);
        Assert.Contains("T4", resolved);
    }

    [Fact]
    public void ResolveReferenceSet_UnmatchedAlternative_NotIncluded()
    {
        // Agent cited neither Required nor the alternative -- the alternative
        // set must not be silently added to the reference set (that would
        // make an unrelated agent citation look "grounded" by coincidence).
        var claim = MaterialClaim("C1", new[] { "T9" }, new ReferenceEvidence(
            Required: new[] { "T1" },
            AcceptableAlternatives: new[] { new[] { "T3", "T4" } }));

        var resolved = ClaimLevelScoring.ResolveReferenceSet(claim);
        Assert.DoesNotContain("T3", resolved);
        Assert.DoesNotContain("T4", resolved);
    }

    // -- Score --

    [Fact]
    public void Score_PerfectMatch_PrecisionRecallBothOne()
    {
        var claim = MaterialClaim("C1", new[] { "T1", "T2" }, new ReferenceEvidence(Required: new[] { "T1", "T2" }));
        var score = ClaimLevelScoring.Score(claim);

        Assert.Equal(1.0, score.Precision);
        Assert.Equal(1.0, score.Recall);
        Assert.True(score.Supported);
    }

    [Fact]
    public void Score_ExtraIrrelevantCitation_LowersPrecisionNotRecall()
    {
        var claim = MaterialClaim("C1", new[] { "T1", "T99" }, new ReferenceEvidence(Required: new[] { "T1" }));
        var score = ClaimLevelScoring.Score(claim);

        Assert.Equal(0.5, score.Precision); // 1 of 2 cited is relevant
        Assert.Equal(1.0, score.Recall); // all required evidence was found
    }

    [Fact]
    public void Score_MissingRequiredEvidence_LowersRecallNotPrecision()
    {
        var claim = MaterialClaim("C1", new[] { "T1" }, new ReferenceEvidence(Required: new[] { "T1", "T2" }));
        var score = ClaimLevelScoring.Score(claim);

        Assert.Equal(1.0, score.Precision); // what was cited is correct
        Assert.Equal(0.5, score.Recall); // only half the required evidence found
    }

    [Fact]
    public void Score_NoAgentEvidence_PrecisionNullRecallZero()
    {
        var claim = MaterialClaim("C1", Array.Empty<string>(), new ReferenceEvidence(Required: new[] { "T1" }));
        var score = ClaimLevelScoring.Score(claim);

        Assert.Null(score.Precision); // 0/0 undefined
        Assert.Equal(0.0, score.Recall); // 0 of 1 required found
    }

    [Fact]
    public void Score_NoReferenceEvidence_AllFieldsNull()
    {
        var claim = MaterialClaim("C1", new[] { "T1" }, ReferenceEvidence: null);
        var score = ClaimLevelScoring.Score(claim);

        Assert.Null(score.Supported);
        Assert.Null(score.Precision);
        Assert.Null(score.Recall);
    }

    // -- ComputeClaimSupportCoverage --

    [Fact]
    public void ComputeClaimSupportCoverage_MixOfSupportedAndNot()
    {
        var claims = new[]
        {
            MaterialClaim("C1", new[] { "T1" }, new ReferenceEvidence(Required: new[] { "T1" })), // supported
            MaterialClaim("C2", new[] { "T2" }, new ReferenceEvidence(Required: new[] { "T1", "T2" })), // not supported
            MaterialClaim("C3", new[] { "T3" }, new ReferenceEvidence(Required: new[] { "T3" })), // supported
        };

        Assert.Equal(0.6667, ClaimLevelScoring.ComputeClaimSupportCoverage(claims)!.Value, precision: 4);
    }

    [Fact]
    public void ComputeClaimSupportCoverage_UnannotatedClaims_ExcludedFromDenominator()
    {
        var claims = new[]
        {
            MaterialClaim("C1", new[] { "T1" }, new ReferenceEvidence(Required: new[] { "T1" })), // supported, counted
            MaterialClaim("C2", new[] { "T2" }, ReferenceEvidence: null), // not annotated, excluded
        };

        Assert.Equal(1.0, ClaimLevelScoring.ComputeClaimSupportCoverage(claims)); // 1/1, not 1/2
    }

    [Fact]
    public void ComputeClaimSupportCoverage_NonMaterialClaims_Excluded()
    {
        var claims = new[]
        {
            new Claim("C1", "immaterial aside", Material: false, AgentEvidence: Array.Empty<string>(), ReferenceEvidence: new ReferenceEvidence(Required: new[] { "T1" })),
        };

        Assert.Null(ClaimLevelScoring.ComputeClaimSupportCoverage(claims));
    }

    [Fact]
    public void ComputeClaimSupportCoverage_NoScorableClaims_NullNotZero()
    {
        Assert.Null(ClaimLevelScoring.ComputeClaimSupportCoverage(Array.Empty<Claim>()));
    }

    [Fact]
    public void ComputeClaimSupportCoverage_AllSupported_IsOne()
    {
        var claims = new[]
        {
            MaterialClaim("C1", new[] { "T1" }, new ReferenceEvidence(Required: new[] { "T1" })),
            MaterialClaim("C2", new[] { "T2" }, new ReferenceEvidence(Required: new[] { "T2" })),
        };
        Assert.Equal(1.0, ClaimLevelScoring.ComputeClaimSupportCoverage(claims));
    }

    // -- ComputeClaimLevelTraceability (macro aggregation) --

    [Fact]
    public void ComputeClaimLevelTraceability_MacroAveragesAcrossClaims()
    {
        var claims = new[]
        {
            MaterialClaim("C1", new[] { "T1" }, new ReferenceEvidence(Required: new[] { "T1" })), // P=1.0 R=1.0
            MaterialClaim("C2", new[] { "T2", "T99" }, new ReferenceEvidence(Required: new[] { "T2" })), // P=0.5 R=1.0
        };

        var result = ClaimLevelScoring.ComputeClaimLevelTraceability(claims);

        Assert.Equal(2, result.ClaimScores.Count);
        Assert.Equal(0.75, result.MacroPrecision); // average of 1.0 and 0.5
        Assert.Equal(1.0, result.MacroRecall);
        Assert.NotNull(result.MacroF1);
        Assert.Equal(1.0, result.ClaimSupportCoverage); // both claims supported
    }

    [Fact]
    public void ComputeClaimLevelTraceability_EmptyClaimList_AllFieldsNull()
    {
        var result = ClaimLevelScoring.ComputeClaimLevelTraceability(Array.Empty<Claim>());

        Assert.Empty(result.ClaimScores);
        Assert.Null(result.MacroPrecision);
        Assert.Null(result.MacroRecall);
        Assert.Null(result.MacroF1);
        Assert.Null(result.ClaimSupportCoverage);
    }

    [Fact]
    public void ComputeClaimLevelTraceability_IsDeterministic()
    {
        var claims = new[] { MaterialClaim("C1", new[] { "T1" }, new ReferenceEvidence(Required: new[] { "T1" })) };
        var r1 = ClaimLevelScoring.ComputeClaimLevelTraceability(claims);
        var r2 = ClaimLevelScoring.ComputeClaimLevelTraceability(claims);

        Assert.Equal(r1.MacroPrecision, r2.MacroPrecision);
        Assert.Equal(r1.MacroRecall, r2.MacroRecall);
        Assert.Equal(r1.ClaimSupportCoverage, r2.ClaimSupportCoverage);
    }

    [Fact]
    public void ComputeClaimLevelTraceability_DistinctFromReportLevelMicroTraceability()
    {
        // A concrete demonstration that claim-level (macro) and report-level
        // (micro) precision genuinely differ, per
        // docs/evidence-traceability-framework.md's own distinction: one
        // claim cites 1 correct + 3 irrelevant (precision 0.25), another
        // cites 1 correct + 0 irrelevant (precision 1.0). Macro-averaging
        // the two claims (0.625) differs from pooling all citations into one
        // report-level (micro) calculation, which report-level EvidenceScoring
        // would compute over the union instead.
        var claims = new[]
        {
            MaterialClaim("C1", new[] { "T1-001", "T1-097", "T1-098", "T1-099" }, new ReferenceEvidence(Required: new[] { "T1-001" })), // P=0.25
            MaterialClaim("C2", new[] { "T2-001" }, new ReferenceEvidence(Required: new[] { "T2-001" })), // P=1.0
        };

        var claimLevel = ClaimLevelScoring.ComputeClaimLevelTraceability(claims);
        Assert.Equal(0.625, claimLevel.MacroPrecision); // (0.25 + 1.0) / 2

        var reportLevelValidIds = new HashSet<string> { "T1-001", "T2-001", "T1-097", "T1-098", "T1-099" };
        var reportLevel = EvidenceScoring.ComputeTraceability("T1-001 T1-097 T1-098 T1-099 T2-001", reportLevelValidIds, reportLevelValidIds);
        Assert.Equal(1.0, reportLevel.Precision); // every cited id is "valid" report-wide, so micro precision looks perfect

        Assert.NotEqual(claimLevel.MacroPrecision, reportLevel.Precision);
    }
}
