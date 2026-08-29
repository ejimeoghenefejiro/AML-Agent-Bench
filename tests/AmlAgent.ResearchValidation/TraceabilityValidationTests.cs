using System.Text.Json.Nodes;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Validates AmlAgent.Evidence.EvidenceScoring.ComputeTraceability against
/// manually-authored gold examples in validation/gold/traceability/*.json.
/// Precision/recall/F1 expectations were hand-computed from each scenario's
/// description, independent of running the code.
///
/// Two gold files here (04_fabricated_evidence_ids, 10_cross_source_evidence_*)
/// document real, substantive gaps between the metric's current behaviour and
/// what a naive reading of "evidence traceability" might expect -- see their
/// "flag" fields. The tests still assert the CURRENT behaviour (so they can
/// meaningfully fail if that behaviour drifts unintentionally), while the gold
/// file is where the scientific caveat is recorded for methodological review.
/// </summary>
public class TraceabilityValidationTests
{
    private static readonly string GoldDir = Path.Combine(AppContext.BaseDirectory, "validation", "gold", "traceability");

    public static IEnumerable<object[]> GoldFiles() =>
        Directory.GetFiles(GoldDir, "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(GoldFiles))]
    public void ComputeTraceability_MatchesManuallySpecifiedGoldExpectation(string goldPath)
    {
        var gold = JsonNode.Parse(File.ReadAllText(goldPath))!.AsObject();
        var scenario = (string)gold["scenario"]!;

        var reportText = (string)gold["report_text"]!;
        var validTxnIds = new HashSet<string>(gold["valid_txn_ids"]!.AsArray().Select(n => (string)n!), StringComparer.OrdinalIgnoreCase);
        var goldTxnIds = new HashSet<string>(gold["gold_txn_ids"]!.AsArray().Select(n => (string)n!), StringComparer.OrdinalIgnoreCase);

        var result = EvidenceScoring.ComputeTraceability(reportText, validTxnIds, goldTxnIds);

        var expected = gold["expected"]!.AsObject();
        Assert.True((int)expected["cited_total"]! == result.CitedTotal, $"[{scenario}] cited_total");
        Assert.True((int)expected["cited_distinct"]! == result.CitedDistinct, $"[{scenario}] cited_distinct");
        Assert.True((int)expected["grounded_distinct"]! == result.GroundedDistinct, $"[{scenario}] grounded_distinct");
        Assert.True((int)expected["gold_total"]! == result.GoldTotal, $"[{scenario}] gold_total");
        Assert.True((int)expected["matched_gold_citations"]! == result.MatchedGoldCitations, $"[{scenario}] matched_gold_citations");

        var expectedFabricated = expected["fabricated_citations"]!.AsArray().Select(n => (string)n!).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var actualFabricated = result.FabricatedCitations.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(expectedFabricated, actualFabricated, StringComparer.OrdinalIgnoreCase);

        var expectedMissing = expected["missing_gold_citations_list"]!.AsArray().Select(n => (string)n!).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var actualMissing = result.MissingGoldCitationsList.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(expectedMissing, actualMissing, StringComparer.OrdinalIgnoreCase);

        AssertNullableEqual(expected["precision"], result.Precision, $"[{scenario}] precision");
        AssertNullableEqual(expected["recall"], result.Recall, $"[{scenario}] recall");
        AssertNullableEqual(expected["f1"], result.F1, $"[{scenario}] f1");

        // Fix #4: valid_evidence_precision/valid_evidence_f1 are optional in
        // older fixtures (absent means "not asserted here", not "expected
        // null" -- unlike precision/recall/f1 above, which every fixture has
        // always carried). Every fixture written after this fix's tests were
        // added below carries these explicitly.
        if (expected.ContainsKey("valid_evidence_precision"))
            AssertNullableEqual(expected["valid_evidence_precision"], result.ValidEvidencePrecision, $"[{scenario}] valid_evidence_precision");
        if (expected.ContainsKey("valid_evidence_f1"))
            AssertNullableEqual(expected["valid_evidence_f1"], result.ValidEvidenceF1, $"[{scenario}] valid_evidence_f1");
    }

    private static void AssertNullableEqual(JsonNode? expectedNode, double? actual, string context)
    {
        if (expectedNode is null)
        {
            Assert.True(actual is null, $"{context}: expected null, got {actual}");
            return;
        }
        Assert.True(actual is not null, $"{context}: expected {(double)expectedNode}, got null");
        Assert.Equal((double)expectedNode, actual!.Value, precision: 4);
    }

    [Fact]
    public void AllTenRequiredScenariosArePresent()
    {
        var scenarios = Directory.GetFiles(GoldDir, "*.json")
            .Select(f => (string)JsonNode.Parse(File.ReadAllText(f))!["scenario"]!)
            .ToHashSet();

        var required = new[]
        {
            "perfect_precision_and_recall", "high_precision_low_recall", "low_precision_high_recall",
            "fabricated_evidence_ids", "missing_evidence", "irrelevant_evidence", "duplicate_citations",
            "correct_record_incorrect_conclusion", "correct_conclusion_multiple_records",
            "cross_source_evidence_transaction_kyc_graph_watchlist",
        };

        foreach (var r in required)
            Assert.Contains(r, scenarios);
    }

    // -- Focused pins on the two flagged definitional gaps --

    [Fact]
    public void FabricatedCitation_ReducesStandardPrecisionButNotValidEvidencePrecisionOrRecall()
    {
        // Pins the resolution of the finding in 04_fabricated_evidence_ids.json
        // (fix #4): the metric now reports precision two ways rather than
        // silently picking one. Standard precision (Precision/F1) counts
        // fabricated citations against the denominator, so it DOES drop when
        // an agent fabricates evidence. ValidEvidencePrecision/ValidEvidenceF1
        // preserve the original "conditional on real citations only" formula,
        // so THOSE stay unaffected by fabrication -- exactly as before this
        // fix, just under an explicit name instead of the plain "precision"
        // field. Recall is unaffected either way (fabricated ids can never be
        // gold, so they never touch the recall denominator or numerator).
        var withoutFabrication = EvidenceScoring.ComputeTraceability(
            "T1-001", new HashSet<string> { "T1-001" }, new HashSet<string> { "T1-001" });
        var withFabrication = EvidenceScoring.ComputeTraceability(
            "T1-001 and T3-999", new HashSet<string> { "T1-001" }, new HashSet<string> { "T1-001" });

        Assert.Equal(withoutFabrication.ValidEvidencePrecision, withFabrication.ValidEvidencePrecision);
        Assert.Equal(withoutFabrication.ValidEvidenceF1, withFabrication.ValidEvidenceF1);
        Assert.Equal(withoutFabrication.Recall, withFabrication.Recall);
        Assert.Single(withFabrication.FabricatedCitations);

        Assert.Equal(1.0, withoutFabrication.Precision);
        Assert.Equal(0.5, withFabrication.Precision); // 1 matched / 2 distinct cited (fabrication counted)
        Assert.True(withFabrication.Precision < withoutFabrication.Precision);
    }

    [Fact]
    public void NonTransactionShapedCitation_IsInvisibleToTraceability()
    {
        // Pins the finding in 10_cross_source_evidence_*.json: a relationship id
        // is simply never extracted, so citing it changes nothing about the score.
        var withoutRelationshipCitation = EvidenceScoring.ComputeTraceability(
            "T1-001", new HashSet<string> { "T1-001" }, new HashSet<string> { "T1-001" });
        var withRelationshipCitation = EvidenceScoring.ComputeTraceability(
            "T1-001, corroborated by relationship R1", new HashSet<string> { "T1-001" }, new HashSet<string> { "T1-001" });

        Assert.Equal(withoutRelationshipCitation.CitedTotal, withRelationshipCitation.CitedTotal);
        Assert.Equal(withoutRelationshipCitation.Precision, withRelationshipCitation.Precision);
        Assert.Equal(withoutRelationshipCitation.Recall, withRelationshipCitation.Recall);
        Assert.DoesNotContain("R1", EvidenceScoring.ExtractCitedTxnIds("corroborated by relationship R1"));
    }

    [Fact]
    public void DuplicateCitations_DoNotInflatePrecisionOrRecall()
    {
        var single = EvidenceScoring.ComputeTraceability("T1-001", new HashSet<string> { "T1-001" }, new HashSet<string> { "T1-001" });
        var repeated = EvidenceScoring.ComputeTraceability("T1-001 T1-001 T1-001", new HashSet<string> { "T1-001" }, new HashSet<string> { "T1-001" });

        Assert.Equal(single.Precision, repeated.Precision);
        Assert.Equal(single.Recall, repeated.Recall);
        Assert.True(repeated.CitedTotal > single.CitedTotal);
        Assert.Equal(single.CitedDistinct, repeated.CitedDistinct);
    }
}
