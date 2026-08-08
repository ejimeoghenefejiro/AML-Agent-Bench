using System.Text.Json.Nodes;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Export;
using AmlAgent.Adapters.Formats;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class CanonicalCaseExporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aml-export-test-{Guid.NewGuid():N}");

    public CanonicalCaseExporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static SourceLineage Lineage(string id) => new("csv", "f.csv", null, id, "csv", "1.0.0");

    private static CanonicalTransaction Txn(string id) => new(
        id, "A1", "A2", 4500.00m, "USD", new DateTimeOffset(2026, 1, 19, 10, 0, 0, TimeSpan.Zero),
        "wire", "US", true, Lineage(id));

    private static CanonicalAmlCase CaseWith(
        IReadOnlyList<CanonicalTransaction>? transactions = null,
        IReadOnlyList<CanonicalEntity>? entities = null,
        IReadOnlyList<CanonicalRelationship>? relationships = null,
        IReadOnlyList<CanonicalEvidence>? evidence = null) => new(
        CanonicalSchema.Version,
        transactions ?? Array.Empty<CanonicalTransaction>(),
        Array.Empty<CanonicalAccount>(), Array.Empty<CanonicalCustomer>(),
        entities ?? Array.Empty<CanonicalEntity>(), relationships ?? Array.Empty<CanonicalRelationship>(),
        Array.Empty<CanonicalCase>(), Array.Empty<CanonicalAlert>(),
        evidence ?? Array.Empty<CanonicalEvidence>(),
        Array.Empty<CanonicalJurisdiction>(), Array.Empty<CanonicalSar>(),
        Array.Empty<MergeConflict>(), Array.Empty<SourceManifestEntry>());

    [Fact]
    public void ExportToDirectory_EmptyCase_WritesNoFiles()
    {
        CanonicalCaseExporter.ExportToDirectory(CaseWith(), _dir);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void ExportToDirectory_Transactions_WritesTransactionsCsvWithTxnIdHeader()
    {
        CanonicalCaseExporter.ExportToDirectory(CaseWith(transactions: new[] { Txn("T1") }), _dir);

        var path = Path.Combine(_dir, "transactions.csv");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.StartsWith("txn_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked", content);
        Assert.Contains("T1,A1,A2,4500", content);
    }

    [Fact]
    public async Task ExportToDirectory_TransactionsCsv_IsRoundTrippableThroughCsvDataAdapter()
    {
        CanonicalCaseExporter.ExportToDirectory(CaseWith(transactions: new[] { Txn("T1"), Txn("T2") }), _dir);

        var adapter = new CsvDataAdapter();
        var reloaded = await adapter.LoadAsync(new DataSourceConfiguration("csv", Path: Path.Combine(_dir, "transactions.csv")));

        Assert.Equal(2, reloaded.Transactions.Count);
        var t1 = reloaded.Transactions.Single(t => t.TransactionId == "T1");
        Assert.Equal(4500.00m, t1.Amount);
        Assert.Equal("USD", t1.Currency);
        Assert.True(t1.SarLinked);
    }

    [Fact]
    public void ExportToDirectory_TransactionsCsv_IsDeterministicallyOrdered()
    {
        CanonicalCaseExporter.ExportToDirectory(CaseWith(transactions: new[] { Txn("T2"), Txn("T1") }), _dir);
        var lines = File.ReadAllLines(Path.Combine(_dir, "transactions.csv"));
        Assert.StartsWith("T1,", lines[1]);
        Assert.StartsWith("T2,", lines[2]);
    }

    [Fact]
    public void ExportToDirectory_NoTransactions_DoesNotWriteTransactionsCsv()
    {
        CanonicalCaseExporter.ExportToDirectory(CaseWith(entities: new[] { new CanonicalEntity("A1", "Account", "A1", Lineage("A1")) }), _dir);
        Assert.False(File.Exists(Path.Combine(_dir, "transactions.csv")));
    }

    [Fact]
    public void ExportToDirectory_EntitiesAndRelationships_WritesRelationshipsJson()
    {
        var entities = new[] { new CanonicalEntity("A100", "Account", "Victim", Lineage("A100")) };
        var relationships = new[] { new CanonicalRelationship("R1", "A100", "A200", "transferred_to", new[] { "T1" }, Lineage("R1")) };
        CanonicalCaseExporter.ExportToDirectory(CaseWith(entities: entities, relationships: relationships), _dir);

        var path = Path.Combine(_dir, "relationships.json");
        Assert.True(File.Exists(path));
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Single(json["entities"]!.AsArray());
        Assert.Single(json["relationships"]!.AsArray());
        Assert.Equal("A100", json["entities"]![0]!["entity_id"]!.GetValue<string>());
        Assert.Equal("R1", json["relationships"]![0]!["relationship_id"]!.GetValue<string>());
    }

    [Fact]
    public void ExportToDirectory_Evidence_WritesEvidenceJsonWithRelatedRecordIds()
    {
        var evidence = new[] { new CanonicalEvidence("EV1", "document", "a statement", new[] { "T1", "T2" }, Lineage("EV1")) };
        CanonicalCaseExporter.ExportToDirectory(CaseWith(evidence: evidence), _dir);

        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(_dir, "evidence.json")))!.AsArray();
        Assert.Single(json);
        Assert.Equal("EV1", json[0]!["evidence_id"]!.GetValue<string>());
        Assert.Equal(2, json[0]!["related_record_ids"]!.AsArray().Count);
    }

    [Fact]
    public void ExportToDirectory_CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_dir, "nested", "data");
        CanonicalCaseExporter.ExportToDirectory(CaseWith(transactions: new[] { Txn("T1") }), nested);
        Assert.True(File.Exists(Path.Combine(nested, "transactions.csv")));
    }
}
