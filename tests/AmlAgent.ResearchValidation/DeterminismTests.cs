using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Normalisation;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Item 14: explicit proof that the parts of the benchmark that are supposed to
/// be deterministic actually are, given identical canonical inputs -- each
/// computation is run REPEAT_COUNT times and every run must agree exactly.
///
/// Deliberately NOT covered here, because it genuinely is not deterministic:
/// the LLM judge's claim extraction and rubric scoring (a live model call).
/// That is the stochastic half of the benchmark, and its actual variability is
/// the subject of item 7 (LLM-as-judge repeatability), which requires live judge
/// calls this deterministic test class does not make. Separating the two lists
/// explicitly is the point of this item, not an oversight.
/// </summary>
public class DeterminismTests
{
    private const int RepeatCount = 10;

    private static SourceLineage Lineage(string id) => new("csv", "f.csv", null, id, "csv", "1.0.0");

    private static CanonicalTransaction Txn(string id, decimal amount = 100m) => new(
        id, "A1", "A2", amount, "USD", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
        "wire", "US", true, Lineage(id));

    private static CanonicalAmlCase SampleCase() => CanonicalCaseMerger.Merge(new[]
    {
        CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2") } },
        CanonicalAmlDataset.Empty() with
        {
            Entities = new[] { new CanonicalEntity("A1", "Account", "A1", Lineage("A1")), new CanonicalEntity("A2", "Account", "A2", Lineage("A2")) },
            Relationships = new[] { new CanonicalRelationship("R1", "A1", "A2", "transferred_to", new[] { "T1", "T-GHOST" }, Lineage("R1")) },
        },
    });

    [Fact]
    public void CanonicalHashes_AreDeterministic()
    {
        var dataset = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2") } };
        var hashes = Enumerable.Range(0, RepeatCount).Select(_ => CanonicalHashing.ComputeNormalisationHash(dataset)).ToList();
        Assert.Single(hashes.Distinct());

        var amlCase = SampleCase();
        var caseHashes = Enumerable.Range(0, RepeatCount).Select(_ => CanonicalHashing.ComputeCaseHash(amlCase)).ToList();
        Assert.Single(caseHashes.Distinct());
    }

    [Fact]
    public void EvidenceReferenceValidation_IsDeterministic()
    {
        var amlCase = SampleCase();
        var results = Enumerable.Range(0, RepeatCount).Select(_ => EvidenceIntegrityValidator.Validate(amlCase)).ToList();

        Assert.All(results, r => Assert.Equal(results[0].Status, r.Status));
        Assert.All(results, r => Assert.Equal(results[0].DanglingReferences.Count, r.DanglingReferences.Count));
        Assert.All(results, r => Assert.Equal(results[0].DanglingReferences[0].EvidenceId, r.DanglingReferences[0].EvidenceId));
    }

    [Fact]
    public void CitationPrecisionRecallF1_AreDeterministic_ForFixedInputs()
    {
        const string reportText = "T1-001 and T1-002 confirm the transfer, alongside a fabricated T1-099.";
        var validIds = new HashSet<string> { "T1-001", "T1-002" };
        var goldIds = new HashSet<string> { "T1-001", "T1-002" };

        var results = Enumerable.Range(0, RepeatCount)
            .Select(_ => EvidenceScoring.ComputeTraceability(reportText, validIds, goldIds))
            .ToList();

        Assert.All(results, r => Assert.Equal(results[0].Precision, r.Precision));
        Assert.All(results, r => Assert.Equal(results[0].Recall, r.Recall));
        Assert.All(results, r => Assert.Equal(results[0].F1, r.F1));
    }

    [Fact]
    public void FabricatedIdDetection_IsDeterministic()
    {
        const string reportText = "T1-001 is real; T1-099 is not.";
        var validIds = new HashSet<string> { "T1-001" };

        var results = Enumerable.Range(0, RepeatCount)
            .Select(_ => EvidenceScoring.ComputeTraceability(reportText, validIds, validIds).FabricatedCitations)
            .ToList();

        Assert.All(results, r => Assert.Equal(results[0], r));
        Assert.All(results, r => Assert.Single(r));
        Assert.All(results, r => Assert.Equal("T1-099", r[0]));
    }

    [Fact]
    public void CaseIntegrityResult_IsDeterministic()
    {
        var assessments = Enumerable.Range(0, RepeatCount)
            .Select(_ => AssuranceEngine.EvaluateCaseIntegrity(casePresent: true, invalidSourceEvidenceReferenceCount: 1, brokenCanonicalEvidenceLineageCount: 0))
            .ToList();

        Assert.All(assessments, a => Assert.Equal(assessments[0].Reasons.Count, a.Reasons.Count));
        Assert.All(assessments, a => Assert.Equal(assessments[0].Reasons[0].Metric, a.Reasons[0].Metric));
        Assert.All(assessments, a => Assert.Equal(assessments[0].Present, a.Present));
    }

    [Fact]
    public void PolicyEvaluation_IsDeterministic_ForFixedMetricInputs()
    {
        var threshold = new MetricThreshold("eghr_rate", "EGHR", "lower_is_better", 0.05, "rate");
        var decisions = Enumerable.Range(0, RepeatCount)
            .Select(_ =>
            {
                var result = AssuranceEngine.EvaluateMetric(threshold, 0.10); // deliberately over threshold
                return AssuranceEngine.Decide(new[] { result }, Array.Empty<string>());
            })
            .ToList();

        Assert.All(decisions, d => Assert.Equal(decisions[0].Overall, d.Overall));
        Assert.All(decisions, d => Assert.Equal(decisions[0].Reason, d.Reason));
        Assert.All(decisions, d => Assert.Equal("NOT_READY_FOR_DEPLOYMENT", d.Overall));
    }

    [Fact]
    public void PolicyEvaluation_WithCaseIntegrityGate_IsDeterministicEndToEnd()
    {
        // The full chain a real assurance run exercises, repeated end to end.
        var threshold = new MetricThreshold("f1", "F1", "higher_is_better", 0.90, "rate");
        var overallResults = Enumerable.Range(0, RepeatCount)
            .Select(_ =>
            {
                var metricResult = AssuranceEngine.EvaluateMetric(threshold, 1.0);
                var decision = AssuranceEngine.Decide(new[] { metricResult }, Array.Empty<string>());
                var caseIntegrity = AssuranceEngine.EvaluateCaseIntegrity(true, invalidSourceEvidenceReferenceCount: 1, brokenCanonicalEvidenceLineageCount: 0);
                return AssuranceEngine.ApplyCaseIntegrityGate(decision, caseIntegrity).Overall;
            })
            .ToList();

        Assert.Single(overallResults.Distinct());
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", overallResults[0]);
    }
}
