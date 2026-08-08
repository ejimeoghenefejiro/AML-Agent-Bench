using AmlAgent.Adapters;
using AmlAgent.Adapters.Manifest;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests.Adapters;

/// <summary>
/// Proves the full chain a real harness run exercises: CaseLoader merges
/// sources -&gt; CaseManifestBuilder writes the real case_manifest.json shape
/// -&gt; that shape's evidence_integrity counts feed AssuranceEngine's
/// case-integrity gate -&gt; the assurance decision is correctly blocked.
/// AssuranceProfileBuilder itself (Harness layer) does this same count
/// extraction against the JSON file on disk; this test proves the counts it
/// would extract are correct against a genuinely-produced manifest, not a
/// hand-typed fixture standing in for one.
/// </summary>
public class AssuranceCaseIntegrityIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best effort */ }
    }

    private string WriteTemp(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aml-assurance-case-test-{Guid.NewGuid():N}.{extension}");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static (int InvalidRefCount, int BrokenLineageCount) ExtractCounts(System.Text.Json.Nodes.JsonObject manifest)
    {
        // Mirrors AssuranceProfileBuilder.EvaluateCaseEvidenceIntegrity's extraction exactly.
        var integrity = manifest["evidence_integrity"]!.AsObject();
        var dangling = integrity["dangling_references"]!.AsArray().Count;
        var missingTxn = integrity["missing_transaction_references"]!.AsArray().Count;
        var incompatible = integrity["incompatible_evidence_types"]!.AsArray().Count;
        var duplicates = integrity["duplicate_evidence_ids"]!.AsArray().Count;
        return (dangling + missingTxn + incompatible, duplicates);
    }

    [Fact]
    public async Task RealCaseManifest_CleanCase_GatesDoesNotBlockAssuranceDecision()
    {
        var csvPath = WriteTemp(
            "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
            "T1,A1,A2,100.00,USD,2026-01-19T10:00:00Z,wire,US,false\n", "csv");
        var def = new CaseDefinition("case-clean", new[] { new CaseSourceDefinition("csv", "transactions", Path: csvPath) });

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());
        var manifest = CaseManifestBuilder.Build(result, DateTimeOffset.UtcNow);
        var (invalidRefCount, brokenLineageCount) = ExtractCounts(manifest);

        var assessment = AssuranceEngine.EvaluateCaseIntegrity(true, invalidRefCount, brokenLineageCount);
        var baseDecision = AssuranceEngine.Decide(
            new[] { AssuranceEngine.EvaluateMetric(new MetricThreshold("f1", "F1", "higher_is_better", 0.9, "rate"), 0.95) },
            Array.Empty<string>());
        var gated = AssuranceEngine.ApplyCaseIntegrityGate(baseDecision, assessment);

        Assert.Equal("PASS", gated.Overall);
    }

    [Fact]
    public async Task RealCaseManifest_DanglingReference_BlocksAssuranceDecisionEvenWithPerfectMetrics()
    {
        var graphMlWithDanglingRef = """
        <graphml xmlns="http://graphml.graphdrawing.org/xmlns">
          <graph id="G" edgedefault="directed">
            <node id="A100"><data key="label">Account</data></node>
            <node id="A200"><data key="label">Account</data></node>
            <edge id="R1" source="A100" target="A200"><data key="evidence_ids">T99999</data></edge>
          </graph>
        </graphml>
        """;
        var graphPath = WriteTemp(graphMlWithDanglingRef, "graphml");
        var def = new CaseDefinition("case-dangling", new[] { new CaseSourceDefinition("graphml", "relationships", Path: graphPath) });

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());
        Assert.False(result.EvidenceIntegrity.Passed); // sanity check on the fixture itself

        var manifest = CaseManifestBuilder.Build(result, DateTimeOffset.UtcNow);
        var (invalidRefCount, brokenLineageCount) = ExtractCounts(manifest);
        Assert.Equal(1, invalidRefCount);
        Assert.Equal(0, brokenLineageCount);

        var assessment = AssuranceEngine.EvaluateCaseIntegrity(true, invalidRefCount, brokenLineageCount);
        var baseDecision = AssuranceEngine.Decide(
            new[] { AssuranceEngine.EvaluateMetric(new MetricThreshold("f1", "F1", "higher_is_better", 0.9, "rate"), 1.0) },
            Array.Empty<string>());
        Assert.Equal("PASS", baseDecision.Overall); // metrics alone would pass

        var gated = AssuranceEngine.ApplyCaseIntegrityGate(baseDecision, assessment);

        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", gated.Overall);
        Assert.Contains(gated.Reasons, r => r.Metric == "case_evidence_integrity.invalid_source_evidence_reference");
    }

    [Fact]
    public async Task RealCaseManifest_TransactionLevelConflict_DoesNotTripEvidenceIntegrityGate()
    {
        // A cross-source disagreement on a transaction's fields is reported via
        // merge_conflicts (RecordType "transaction"), which is a distinct concept
        // from evidence_integrity.duplicate_evidence_ids (RecordType "evidence").
        // This proves the two stay distinct rather than any merge conflict
        // masquerading as "broken canonical evidence lineage".
        var csvA = WriteTemp(
            "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
            "T1,A1,A2,100.00,USD,2026-01-19T10:00:00Z,wire,US,false\n", "csv");
        var csvB = WriteTemp(
            "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
            "T1,A1,A2,999.00,USD,2026-01-19T10:00:00Z,wire,US,false\n", "csv");
        var def = new CaseDefinition("case-conflict", new[]
        {
            new CaseSourceDefinition("csv", "transactions-a", Path: csvA),
            new CaseSourceDefinition("csv", "transactions-b", Path: csvB),
        });

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());
        Assert.Single(result.MergedCase.Conflicts); // sanity check: this is a transaction conflict, not evidence

        // This case exercises the invalid_source_evidence_reference / clean path only
        // (transaction conflicts are reported via merge_conflicts, not
        // evidence_integrity.duplicate_evidence_ids -- confirming the two stay
        // distinct rather than a transaction conflict masquerading as broken lineage).
        var manifest = CaseManifestBuilder.Build(result, DateTimeOffset.UtcNow);
        var (invalidRefCount, brokenLineageCount) = ExtractCounts(manifest);
        Assert.Equal(0, invalidRefCount);
        Assert.Equal(0, brokenLineageCount);

        var assessment = AssuranceEngine.EvaluateCaseIntegrity(true, invalidRefCount, brokenLineageCount);
        Assert.Empty(assessment.Reasons); // a transaction-level conflict alone does not trip the evidence-integrity gate
    }
}
