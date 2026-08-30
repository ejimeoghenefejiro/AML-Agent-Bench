using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Graded sensitivity ladders (v0.3 validation-priorities item 8) --
/// construct validity evidence going beyond the mostly-binary before/after
/// comparisons elsewhere in this test suite. Each ladder progressively
/// increases one specific perturbation (gold-evidence omission, invalid
/// references, irrelevant-but-real evidence, unsupported material claims,
/// incomplete multi-hop evidence) and asserts the relevant metric moves
/// monotonically in the theoretically-expected direction at every step, not
/// just at the two endpoints. Pure unit tests against
/// AmlAgent.Evidence.EvidenceScoring/ClaimLevelScoring directly -- no
/// workspace, no LLM, no fabricated human data; every input here is
/// synthetic and constructed specifically to exercise one perturbation
/// dimension in isolation, holding everything else fixed.
/// </summary>
public class SensitivityLadderTests
{
    private static EvidenceReference Ref(string id, string type = "transaction") => new(id, type, "csv");

    // -- Ladder 1: gold evidence omission (0%, 25%, 50%, 75%, 100%) -- recall should decrease --

    [Fact]
    public void GoldEvidenceOmissionLadder_RecallDecreasesMonotonically()
    {
        var valid = new[] { Ref("G1"), Ref("G2"), Ref("G3"), Ref("G4") };
        var gold = valid; // all four are gold-relevant

        // Step k: the report cites only the first (4-k) of the four gold ids
        // -- 0/1/2/3/4 omitted = 0%/25%/50%/75%/100% omission.
        var citedTextByStep = new[]
        {
            "G1 G2 G3 G4", // 0% omitted
            "G1 G2 G3",    // 25% omitted
            "G1 G2",       // 50% omitted
            "G1",          // 75% omitted
            "",            // 100% omitted
        };
        var expectedRecall = new double?[] { 1.0, 0.75, 0.5, 0.25, 0.0 };

        var recalls = citedTextByStep.Select(text => EvidenceScoring.ComputeTraceability(text, valid, gold).Recall).ToList();

        Assert.Equal(expectedRecall, recalls);
        AssertMonotonicNonIncreasing(recalls.Select(r => r!.Value).ToList(), "recall");
    }

    // -- Ladder 2: invalid (fabricated) references (0, 1, 3, 5) -- reference validity and standard precision should decrease --

    [Fact]
    public void InvalidReferenceCountLadder_ReferenceValidityAndStandardPrecisionDecreaseMonotonically_ValidEvidencePrecisionUnaffected()
    {
        // Real evidence: R1, R2, R3 -- always cited, always gold. Fabricated
        // ids (R91..R95) use the SAME shape ("R\d+", inferred from the real
        // R1-R3 by fix #3's InferEvidenceIdShapes) so this ladder also
        // exercises the generic fabrication-detection path, not just the
        // legacy transaction-shape regex.
        var valid = new[] { Ref("R1", "relationship"), Ref("R2", "relationship"), Ref("R3", "relationship") };
        var gold = valid;
        var fabricatedIds = new[] { "R91", "R92", "R93", "R94", "R95" };

        var invalidCounts = new[] { 0, 1, 3, 5 };
        var results = invalidCounts.Select(k =>
        {
            var text = "R1 R2 R3 " + string.Join(" ", fabricatedIds.Take(k));
            return EvidenceScoring.ComputeTraceability(text, valid, gold);
        }).ToList();

        // Reference validity rate = grounded / citedDistinct = 3 / (3+k).
        var referenceValidityRates = results.Select(r => (double)r.GroundedDistinct / r.CitedDistinct).ToList();
        Assert.Equal(new[] { 1.0, 0.75, 0.5, 0.375 }, referenceValidityRates);
        AssertMonotonicNonIncreasing(referenceValidityRates, "reference validity rate");

        // Standard precision (fix #4) counts fabrications against the denominator -- same shape of decline.
        var standardPrecision = results.Select(r => r.Precision!.Value).ToList();
        Assert.Equal(new[] { 1.0, 0.75, 0.5, 0.375 }, standardPrecision);
        AssertMonotonicNonIncreasing(standardPrecision, "standard precision");

        // Valid-evidence precision (fix #4) is deliberately blind to fabrication -- flat at 1.0 throughout.
        foreach (var r in results)
            Assert.Equal(1.0, r.ValidEvidencePrecision);

        // Fabricated citation count itself increases monotonically, 1:1 with k.
        var fabricatedCounts = results.Select(r => r.FabricatedCitations.Count).ToList();
        Assert.Equal(invalidCounts, fabricatedCounts);
    }

    // -- Ladder 3: increasing irrelevant-but-real (distractor) evidence -- precision should decrease, recall unaffected --

    [Fact]
    public void DistractorEvidenceVolumeLadder_PrecisionDecreasesMonotonically_RecallUnaffected()
    {
        var goldItem = Ref("V1");
        var distractors = new[] { Ref("D1"), Ref("D2"), Ref("D3"), Ref("D4") };
        var valid = new[] { goldItem }.Concat(distractors).ToList();
        var gold = new[] { goldItem };

        var distractorCounts = new[] { 0, 1, 2, 3, 4 };
        var results = distractorCounts.Select(k =>
        {
            var text = "V1 " + string.Join(" ", distractors.Take(k).Select(d => d.EvidenceId));
            return EvidenceScoring.ComputeTraceability(text, valid, gold);
        }).ToList();

        var precisions = results.Select(r => r.Precision!.Value).ToList();
        Assert.Equal(new[] { 1.0, 0.5, 0.3333, 0.25, 0.2 }, precisions.Select(p => Math.Round(p, 4)));
        AssertMonotonicNonIncreasing(precisions, "precision");

        // Recall is unaffected -- the gold item is always cited regardless of
        // how much irrelevant-but-real evidence accompanies it.
        foreach (var r in results)
            Assert.Equal(1.0, r.Recall);
    }

    // -- Ladder 4: partial claim support (0%, 25%, 50%, 75%, 100%) -- Claim Support Coverage should decrease --

    [Fact]
    public void UnsupportedMaterialClaimsLadder_ClaimSupportCoverageDecreasesMonotonically()
    {
        // Four material claims, each needing exactly one evidence id.
        // supportedCount of them actually have it; the rest have none.
        var supportedCountSteps = new[] { 4, 3, 2, 1, 0 }; // 0%, 25%, 50%, 75%, 100% unsupported
        var coverages = supportedCountSteps.Select(supportedCount =>
        {
            var claims = Enumerable.Range(1, 4).Select(i =>
            {
                var required = new List<string> { $"E{i}" };
                var agentEvidence = i <= supportedCount ? required : new List<string>();
                return new Claim($"MC{i}", $"claim {i}", Material: true, agentEvidence, new ReferenceEvidence(required));
            }).ToList();
            return ClaimLevelScoring.ComputeClaimSupportCoverage(claims);
        }).ToList();

        Assert.Equal(new double?[] { 1.0, 0.75, 0.5, 0.25, 0.0 }, coverages);
        AssertMonotonicNonIncreasing(coverages.Select(c => c!.Value).ToList(), "claim support coverage");
    }

    // -- Ladder 5: increasingly incomplete multi-hop evidence -- claim-level recall should decrease, support should flip to false as soon as any hop is missing --

    [Fact]
    public void IncompleteMultiHopEvidenceLadder_ClaimLevelRecallDecreasesMonotonically_SupportBecomesFalseAssoonAsIncomplete()
    {
        // One claim needing all four hops of a layering chain (E1..E4).
        // providedCount of the four hops are actually cited, progressively fewer.
        var required = new List<string> { "E1", "E2", "E3", "E4" };
        var providedCountSteps = new[] { 4, 3, 2, 1, 0 };

        var scores = providedCountSteps.Select(providedCount =>
        {
            var agentEvidence = required.Take(providedCount).ToList();
            var claim = new Claim("MC1", "multi-hop claim", Material: true, agentEvidence, new ReferenceEvidence(required));
            return ClaimLevelScoring.Score(claim);
        }).ToList();

        var recalls = scores.Select(s => s.Recall!.Value).ToList();
        Assert.Equal(new[] { 1.0, 0.75, 0.5, 0.25, 0.0 }, recalls);
        AssertMonotonicNonIncreasing(recalls, "claim-level recall");

        // Support is a hard "were ALL required hops cited" boolean, not
        // graded -- correctly false the moment even one hop is missing,
        // regardless of how many of the other three are still present. This
        // is the deliberate contrast with recall's gradual decline: the two
        // fields measure different things and must not track each other.
        Assert.True(scores[0].Supported); // all 4 present
        for (int i = 1; i < scores.Count; i++)
            Assert.False(scores[i].Supported, $"step with {providedCountSteps[i]}/4 hops should not be Supported");
    }

    private static void AssertMonotonicNonIncreasing(IReadOnlyList<double> values, string metricName)
    {
        for (int i = 1; i < values.Count; i++)
            Assert.True(values[i] <= values[i - 1],
                $"{metricName} should be monotonically non-increasing, but step {i} ({values[i]}) > step {i - 1} ({values[i - 1]})");
    }
}
