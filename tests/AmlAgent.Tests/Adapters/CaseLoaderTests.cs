using AmlAgent.Adapters;
using AmlAgent.Adapters.Manifest;
using AmlAgent.Adapters.Normalisation;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class CaseLoaderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best effort */ }
    }

    private string WriteTemp(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aml-case-test-{Guid.NewGuid():N}.{extension}");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private const string TxnCsv =
        "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
        "T10021,A100,A812,4500.00,USD,2026-01-19T10:00:00Z,wire,US,true\n" +
        "T10022,A812,A900,4200.00,USD,2026-01-19T11:00:00Z,wire,US,true\n";

    private const string GraphMlValid = """
    <graphml xmlns="http://graphml.graphdrawing.org/xmlns">
      <graph id="G" edgedefault="directed">
        <node id="A100"><data key="label">Account</data></node>
        <node id="A812"><data key="label">Account</data></node>
        <node id="A900"><data key="label">Account</data></node>
        <edge id="R-1001" source="A100" target="A812"><data key="evidence_ids">T10021</data></edge>
        <edge id="R-1002" source="A812" target="A900"><data key="evidence_ids">T10022</data></edge>
      </graph>
    </graphml>
    """;

    private const string GraphMlDangling = """
    <graphml xmlns="http://graphml.graphdrawing.org/xmlns">
      <graph id="G" edgedefault="directed">
        <node id="A100"><data key="label">Account</data></node>
        <node id="A812"><data key="label">Account</data></node>
        <edge id="R-1001" source="A100" target="A812"><data key="evidence_ids">T99999</data></edge>
      </graph>
    </graphml>
    """;

    private CaseDefinition BuildDefinition(string caseId, params (string SourceType, string Path, string Role)[] sources) =>
        new(caseId, sources.Select(s => new CaseSourceDefinition(s.SourceType, s.Role, Path: s.Path)).ToList());

    [Fact]
    public async Task LoadAsync_ValidMultiSourceCase_MergesIntoOneCase()
    {
        var csvPath = WriteTemp(TxnCsv, "csv");
        var graphPath = WriteTemp(GraphMlValid, "graphml");
        var def = BuildDefinition("case-001", ("csv", csvPath, "transactions"), ("graphml", graphPath, "relationships"));

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        Assert.True(result.AllSourcesLoaded);
        Assert.Equal(2, result.MergedCase.Transactions.Count);
        Assert.Equal(3, result.MergedCase.Entities.Count);
        Assert.Equal(2, result.MergedCase.Relationships.Count);
        Assert.Equal(2, result.MergedCase.SourceManifest.Count);
    }

    [Fact]
    public async Task LoadAsync_ValidMultiSourceCase_EvidenceIntegrityPasses()
    {
        var csvPath = WriteTemp(TxnCsv, "csv");
        var graphPath = WriteTemp(GraphMlValid, "graphml");
        var def = BuildDefinition("case-001", ("csv", csvPath, "transactions"), ("graphml", graphPath, "relationships"));

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        Assert.True(result.EvidenceIntegrity.Passed);
    }

    [Fact]
    public async Task LoadAsync_DanglingEvidenceReferenceAcrossSources_FlaggedByIntegrityValidator()
    {
        var csvPath = WriteTemp(TxnCsv, "csv");
        var graphPath = WriteTemp(GraphMlDangling, "graphml");
        var def = BuildDefinition("case-001", ("csv", csvPath, "transactions"), ("graphml", graphPath, "relationships"));

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        Assert.False(result.EvidenceIntegrity.Passed);
        Assert.Single(result.EvidenceIntegrity.DanglingReferences);
        Assert.Equal("T99999", result.EvidenceIntegrity.DanglingReferences[0].EvidenceId);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedSourceType_RecordedAsFailure_DoesNotAbortOtherSources()
    {
        var csvPath = WriteTemp(TxnCsv, "csv");
        var def = new CaseDefinition("case-001", new[]
        {
            new CaseSourceDefinition("csv", "transactions", Path: csvPath),
            new CaseSourceDefinition("xml-legacy", "unknown", Path: "whatever.xml"),
        });

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        Assert.False(result.AllSourcesLoaded);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("xml-legacy", failure.SourceType);
        Assert.Equal(2, result.MergedCase.Transactions.Count); // csv source still loaded and merged
    }

    [Fact]
    public async Task LoadAsync_FileNotFoundSource_RecordedAsFailure_NotThrown()
    {
        var def = BuildDefinition("case-001", ("csv", "does/not/exist.csv", "transactions"));

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        Assert.False(result.AllSourcesLoaded);
        Assert.Single(result.Failures);
        Assert.Empty(result.MergedCase.Transactions);
    }

    [Fact]
    public async Task LoadAsync_MergeConflict_ReportedInResult()
    {
        var csv1 = WriteTemp(TxnCsv, "csv"); // T10021 amount 4500.00
        var conflictingCsv =
            "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
            "T10021,A100,A812,9999.00,USD,2026-01-19T10:00:00Z,wire,US,true\n";
        var csv2 = WriteTemp(conflictingCsv, "csv");
        var def = BuildDefinition("case-001", ("csv", csv1, "transactions"), ("csv", csv2, "transactions-2"));

        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        var conflict = Assert.Single(result.MergedCase.Conflicts);
        Assert.Equal("T10021", conflict.RecordId);
        Assert.Equal("conflicting_value", conflict.ConflictType);
    }

    [Fact]
    public async Task LoadAsync_SameInputs_ProduceSameCanonicalCaseHash()
    {
        var csvPath = WriteTemp(TxnCsv, "csv");
        var graphPath = WriteTemp(GraphMlValid, "graphml");
        var def = BuildDefinition("case-001", ("csv", csvPath, "transactions"), ("graphml", graphPath, "relationships"));

        var result1 = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());
        var result2 = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        var hash1 = CanonicalHashing.ComputeCaseHash(result1.MergedCase);
        var hash2 = CanonicalHashing.ComputeCaseHash(result2.MergedCase);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task LoadAsync_ChangedSource_ProducesDifferentCanonicalCaseHash()
    {
        var csvPath = WriteTemp(TxnCsv, "csv");
        var graphPath = WriteTemp(GraphMlValid, "graphml");
        var def = BuildDefinition("case-001", ("csv", csvPath, "transactions"), ("graphml", graphPath, "relationships"));
        var before = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());
        var hashBefore = CanonicalHashing.ComputeCaseHash(before.MergedCase);

        var changedCsv =
            "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
            "T10021,A100,A812,1.00,USD,2026-01-19T10:00:00Z,wire,US,true\n" +
            "T10022,A812,A900,4200.00,USD,2026-01-19T11:00:00Z,wire,US,true\n";
        var changedCsvPath = WriteTemp(changedCsv, "csv");
        var def2 = BuildDefinition("case-001", ("csv", changedCsvPath, "transactions"), ("graphml", graphPath, "relationships"));
        var after = await CaseLoader.LoadAsync(def2, AdapterRegistry.CreateDefault());
        var hashAfter = CanonicalHashing.ComputeCaseHash(after.MergedCase);

        Assert.NotEqual(hashBefore, hashAfter);
    }

    [Fact]
    public async Task CaseManifestBuilder_Build_IncludesAllRequiredTopLevelFields()
    {
        var csvPath = WriteTemp(TxnCsv, "csv");
        var graphPath = WriteTemp(GraphMlValid, "graphml");
        var def = BuildDefinition("case-001", ("csv", csvPath, "transactions"), ("graphml", graphPath, "relationships"));
        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        var manifest = CaseManifestBuilder.Build(result, DateTimeOffset.UtcNow);

        Assert.Equal("case-001", manifest["case_id"]!.GetValue<string>());
        Assert.NotNull(manifest["schema_version"]);
        Assert.NotNull(manifest["sources"]);
        Assert.NotNull(manifest["source_manifests"]);
        Assert.NotNull(manifest["merge_conflicts"]);
        Assert.NotNull(manifest["evidence_integrity"]);
        Assert.NotNull(manifest["canonical_case_hash"]);
        Assert.NotNull(manifest["generated_at_utc"]);
        Assert.Equal("passed", manifest["evidence_integrity"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task CaseManifestBuilder_CanonicalCaseHash_MatchesDirectComputation()
    {
        var csvPath = WriteTemp(TxnCsv, "csv");
        var def = BuildDefinition("case-001", ("csv", csvPath, "transactions"));
        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        var manifest = CaseManifestBuilder.Build(result, DateTimeOffset.UtcNow);
        var expectedHash = CanonicalHashing.ComputeCaseHash(result.MergedCase);

        Assert.Equal(expectedHash, manifest["canonical_case_hash"]!.GetValue<string>());
    }

    [Fact]
    public async Task CaseManifestBuilder_LoadFailures_NeverSilentlyDropped()
    {
        var def = BuildDefinition("case-001", ("csv", "missing.csv", "transactions"));
        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());

        var manifest = CaseManifestBuilder.Build(result, DateTimeOffset.UtcNow);

        Assert.Single(manifest["load_failures"]!.AsArray());
    }
}
