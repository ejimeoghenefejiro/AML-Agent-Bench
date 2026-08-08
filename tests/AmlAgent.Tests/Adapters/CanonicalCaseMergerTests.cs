using AmlAgent.Adapters.Canonical;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class CanonicalCaseMergerTests
{
    private static SourceLineage Lineage(string sourceType, string id, string adapter = "csv") =>
        new(sourceType, $"{sourceType}-file", null, id, adapter, "1.0.0");

    private static CanonicalTransaction Txn(string id, string sourceType = "csv", decimal amount = 100m,
        string currency = "USD", DateTimeOffset? ts = null) => new(
        TransactionId: id,
        SourceAccount: "A1",
        DestinationAccount: "A2",
        Amount: amount,
        Currency: currency,
        Timestamp: ts ?? new DateTimeOffset(2026, 1, 19, 10, 0, 0, TimeSpan.Zero),
        Channel: "wire",
        Jurisdiction: "US",
        SarLinked: false,
        SourceLineage: Lineage(sourceType, id));

    private static CanonicalEntity Entity(string id, string sourceType = "graphml") => new(
        EntityId: id, EntityType: "Account", DisplayName: id, SourceLineage: Lineage(sourceType, id, "graphml"));

    private static CanonicalRelationship Rel(string id, string sourceId, string targetId, string sourceType = "graphml") => new(
        RelationshipId: id, SourceEntityId: sourceId, TargetEntityId: targetId, RelationshipType: "transferred_to",
        EvidenceIds: Array.Empty<string>(), SourceLineage: Lineage(sourceType, id, "graphml"));

    [Fact]
    public void Merge_NoSources_ReturnsEmptyCase()
    {
        var result = CanonicalCaseMerger.Merge(Array.Empty<CanonicalAmlDataset>());
        Assert.Empty(result.Transactions);
        Assert.Empty(result.Conflicts);
        Assert.Empty(result.SourceManifest);
    }

    [Fact]
    public void Merge_DisjointSources_UnionsAllRecordsWithoutConflicts()
    {
        var csv = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2") } };
        var json = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T3", "json") } };

        var result = CanonicalCaseMerger.Merge(new[] { csv, json });

        Assert.Equal(3, result.Transactions.Count);
        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.SourceManifest.Count);
    }

    [Fact]
    public void Merge_SameIdIdenticalContentAcrossSources_SilentlyDedupedNoConflict()
    {
        var ts = new DateTimeOffset(2026, 1, 19, 10, 0, 0, TimeSpan.Zero);
        var csv = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", "csv", 100m, "USD", ts) } };
        var json = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", "json", 100m, "USD", ts) } };

        var result = CanonicalCaseMerger.Merge(new[] { csv, json });

        Assert.Single(result.Transactions); // deduped, not two rows
        Assert.Empty(result.Conflicts); // agreeing sources are not a conflict
    }

    [Fact]
    public void Merge_SameIdDifferentTimestamp_RecordsTimestampMismatch()
    {
        var csv = CanonicalAmlDataset.Empty() with
        {
            Transactions = new[] { Txn("T1", "csv", ts: new DateTimeOffset(2026, 1, 19, 10, 0, 0, TimeSpan.Zero)) }
        };
        var json = CanonicalAmlDataset.Empty() with
        {
            Transactions = new[] { Txn("T1", "json", ts: new DateTimeOffset(2026, 1, 19, 11, 0, 0, TimeSpan.Zero)) }
        };

        var result = CanonicalCaseMerger.Merge(new[] { csv, json });

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("timestamp_mismatch", conflict.ConflictType);
        Assert.Equal("T1", conflict.RecordId);
    }

    [Fact]
    public void Merge_SameIdDifferentCurrency_RecordsCurrencyMismatch()
    {
        var csv = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", "csv", currency: "USD") } };
        var json = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", "json", currency: "EUR") } };

        var result = CanonicalCaseMerger.Merge(new[] { csv, json });

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("currency_mismatch", conflict.ConflictType);
    }

    [Fact]
    public void Merge_SameIdDifferentAmount_RecordsConflictingValue()
    {
        var csv = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", "csv", amount: 100m) } };
        var json = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", "json", amount: 999m) } };

        var result = CanonicalCaseMerger.Merge(new[] { csv, json });

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("conflicting_value", conflict.ConflictType);
    }

    [Fact]
    public void Merge_FirstSeenValueIsKeptOnConflict()
    {
        var csv = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", "csv", amount: 100m) } };
        var json = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", "json", amount: 999m) } };

        var result = CanonicalCaseMerger.Merge(new[] { csv, json });

        var kept = Assert.Single(result.Transactions);
        Assert.Equal(100m, kept.Amount);
        Assert.Equal("csv", kept.SourceLineage.SourceType);
    }

    [Fact]
    public void Merge_RelationshipReferencingMissingEntity_RecordsMissingReference()
    {
        var graph = CanonicalAmlDataset.Empty() with
        {
            Entities = new[] { Entity("A100") },
            Relationships = new[] { Rel("R1", "A100", "A999") }, // A999 never defined
        };

        var result = CanonicalCaseMerger.Merge(new[] { graph });

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("missing_reference", conflict.ConflictType);
        Assert.Contains("A999", conflict.Description);
    }

    [Fact]
    public void Merge_RelationshipWithBothEndpointsResolved_NoMissingReferenceConflict()
    {
        var graph = CanonicalAmlDataset.Empty() with
        {
            Entities = new[] { Entity("A100"), Entity("A200") },
            Relationships = new[] { Rel("R1", "A100", "A200") },
        };

        var result = CanonicalCaseMerger.Merge(new[] { graph });

        Assert.Empty(result.Conflicts);
        Assert.Single(result.Relationships);
    }

    [Fact]
    public void Merge_EntitiesFromMultipleSourcesResolveRelationshipAcrossSources()
    {
        // Entity A100 comes from a CSV-derived source, A200 from a GraphML source;
        // the relationship (from the GraphML source) must resolve against the union.
        var csvEntities = CanonicalAmlDataset.Empty() with { Entities = new[] { Entity("A100", "csv") } };
        var graph = CanonicalAmlDataset.Empty() with
        {
            Entities = new[] { Entity("A200", "graphml") },
            Relationships = new[] { Rel("R1", "A100", "A200") },
        };

        var result = CanonicalCaseMerger.Merge(new[] { csvEntities, graph });

        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.Entities.Count);
    }

    [Fact]
    public void Merge_IncompatibleSchemaVersion_ExcludesDatasetAndRecordsConflict()
    {
        var v1 = CanonicalAmlDataset.Empty("aml-canonical-1.0") with { Transactions = new[] { Txn("T1") } };
        var v2 = CanonicalAmlDataset.Empty("aml-canonical-2.0") with { Transactions = new[] { Txn("T2", "json") } };

        var result = CanonicalCaseMerger.Merge(new[] { v1, v2 });

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("incompatible_schema", conflict.ConflictType);
        Assert.Single(result.Transactions); // only v1's T1 made it into the merge
        Assert.Equal("T1", result.Transactions[0].TransactionId);
        // still reported in the source manifest, even though excluded from the merge itself
        Assert.Equal(2, result.SourceManifest.Count);
    }

    [Fact]
    public void Merge_SourceManifestRecordsAdapterIdentityAndCounts()
    {
        var csv = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2") } };

        var result = CanonicalCaseMerger.Merge(new[] { csv });

        var entry = Assert.Single(result.SourceManifest);
        Assert.Equal("csv", entry.SourceType);
        Assert.Equal("csv", entry.Adapter);
        Assert.Equal(2, entry.RecordCount);
        Assert.StartsWith("sha256:", entry.DatasetHash);
    }

    [Fact]
    public void Merge_EmptyDatasetContributesManifestEntryWithZeroRecords()
    {
        var result = CanonicalCaseMerger.Merge(new[] { CanonicalAmlDataset.Empty() });
        var entry = Assert.Single(result.SourceManifest);
        Assert.Equal(0, entry.RecordCount);
    }
}
