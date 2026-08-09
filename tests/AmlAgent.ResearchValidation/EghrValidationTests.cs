using System.Text.Json.Nodes;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Aggressive validation of EGHR (Evidence-Grounded Hallucination Rate) against
/// manually-authored gold examples in validation/gold/eghr/*.json -- one file per
/// named scenario (intrinsic/extrinsic hallucination, partial support, fabricated
/// citation, etc). Gold values were computed by hand from the scenario
/// description, not derived from running the code -- these tests can genuinely
/// fail if EvidenceScoring.ScoreClaims's behaviour drifts from its own
/// scientific definition.
///
/// Where a gold file's "flag" field is present, the test still asserts the
/// implementation's actual (current) behaviour -- per the research-validation
/// instructions, tests are not tuned to silently match a definitional gap, but
/// they also should not fail on a documented, deliberate design choice. The gold
/// file itself is where the flag is recorded for methodological review.
/// </summary>
public class EghrValidationTests
{
    private static readonly string GoldDir = Path.Combine(AppContext.BaseDirectory, "validation", "gold", "eghr");

    public static IEnumerable<object[]> GoldFiles() =>
        Directory.GetFiles(GoldDir, "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(GoldFiles))]
    public void ScoreClaims_MatchesManuallySpecifiedGoldExpectation(string goldPath)
    {
        var gold = JsonNode.Parse(File.ReadAllText(goldPath))!.AsObject();
        var scenario = (string)gold["scenario"]!;

        var validTxnIds = new HashSet<string>(
            gold["valid_txn_ids"]!.AsArray().Select(n => (string)n!),
            StringComparer.OrdinalIgnoreCase);

        var claims = gold["claims"]!.AsArray().Select(c => new ClaimInput(
            Text: (string)c!["text"]!,
            CitedTxnIds: c["cited_txn_ids"]!.AsArray().Select(id => (string)id!).ToList(),
            Support: (string)c["judge_support"]!)).ToList();

        var result = EvidenceScoring.ScoreClaims(claims, validTxnIds);

        var expected = gold["expected"]!.AsObject();
        Assert.True((int)expected["total_claims"]! == result.TotalClaims, $"[{scenario}] total_claims");
        Assert.True((int)expected["supported_count"]! == result.SupportedCount, $"[{scenario}] supported_count");
        Assert.True((int)expected["unsupported_count"]! == result.UnsupportedCount, $"[{scenario}] unsupported_count");
        Assert.True((int)expected["contradicted_count"]! == result.ContradictedCount, $"[{scenario}] contradicted_count");
        Assert.Equal((double)expected["eghr_rate"]!, result.Rate, precision: 4);
    }

    [Fact]
    public void AllTenRequiredScenariosArePresent()
    {
        var scenarios = Directory.GetFiles(GoldDir, "*.json")
            .Select(f => (string)JsonNode.Parse(File.ReadAllText(f))!["scenario"]!)
            .ToHashSet();

        var required = new[]
        {
            "zero_unsupported_claims", "one_unsupported_claim", "multiple_unsupported_claims",
            "intrinsic_hallucination", "extrinsic_hallucination", "partially_supported_claim",
            "contradictory_evidence", "correct_claim_wrong_citation", "unsupported_numerical_claim",
            "unsupported_relationship_claim",
        };

        foreach (var r in required)
            Assert.Contains(r, scenarios);
    }

    // -- Focused, code-only tests for properties worth pinning down explicitly
    // beyond what a single gold-file scenario captures --

    [Fact]
    public void EghrRate_ZeroClaims_IsZeroNotNaN()
    {
        var result = EvidenceScoring.ScoreClaims(Array.Empty<ClaimInput>(), new HashSet<string>());
        Assert.Equal(0.0, result.Rate);
        Assert.Equal(0, result.TotalClaims);
    }

    [Fact]
    public void EghrRate_AllClaimsSupported_IsExactlyZero()
    {
        var claims = new[]
        {
            new ClaimInput("a", new[] { "T1" }, "supported"),
            new ClaimInput("b", new[] { "T2" }, "supported"),
        };
        var result = EvidenceScoring.ScoreClaims(claims, new HashSet<string> { "T1", "T2" });
        Assert.Equal(0.0, result.Rate);
    }

    [Fact]
    public void EghrRate_AllClaimsHallucinated_IsExactlyOne()
    {
        var claims = new[]
        {
            new ClaimInput("a", Array.Empty<string>(), "unsupported"),
            new ClaimInput("b", Array.Empty<string>(), "unsupported"),
        };
        var result = EvidenceScoring.ScoreClaims(claims, new HashSet<string>());
        Assert.Equal(1.0, result.Rate);
    }

    [Fact]
    public void FabricatedCitation_OverridesJudgeSupportedLabel_RegardlessOfClaimTruth()
    {
        // Pins down the deterministic-backstop contract explicitly, independent
        // of the gold-file scenario: no combination of judge label can make a
        // fabricated citation count as grounded.
        var claim = new ClaimInput("true fact, wrong citation", new[] { "FAKE-ID" }, "supported");
        var result = EvidenceScoring.ScoreClaims(new[] { claim }, new HashSet<string> { "T1" });

        Assert.Equal(0, result.SupportedCount);
        Assert.Equal(1, result.UnsupportedCount);
        Assert.True(result.Claims[0].FabricatedCitation);
    }

    [Fact]
    public void OutOfVocabularySupportLabel_SilentlyCollapsesToUnsupported()
    {
        // Documents the exact collapse behaviour flagged in
        // validation/gold/eghr/06_partially_supported_claim.json: any support
        // string other than exactly "supported" or "contradicted" (case/whitespace
        // aside) becomes "unsupported", with the original label discarded.
        var claim = new ClaimInput("half right", new[] { "T1" }, "partially_supported");
        var result = EvidenceScoring.ScoreClaims(new[] { claim }, new HashSet<string> { "T1" });

        Assert.Equal("unsupported", result.Claims[0].Support);
        Assert.Equal(1, result.UnsupportedCount);
    }
}
