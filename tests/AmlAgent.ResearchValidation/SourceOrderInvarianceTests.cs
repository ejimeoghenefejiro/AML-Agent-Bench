using AmlAgent.Adapters;
using AmlAgent.Adapters.Normalisation;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Item 5: loads the same multi-source case in different source orders and
/// verifies the canonical case hash. Two scenarios, deliberately contrasted:
///
///   1. A genuinely conflict-free 4-source case (disjoint transaction ids across
///      CSV/JSON/Parquet/GraphML) -- the canonical case hash must be identical
///      across every source ordering, per the instruction's core claim.
///
///   2. task-007-multi-source-mule-network's REAL sources, which have a genuine
///      unresolved cross-source conflict (T2-001's timestamp, JSON vs Parquet).
///      Here order-invariance does NOT hold, by design: CanonicalCaseMerger keeps
///      the first-seen value on a real conflict, so whichever source is loaded
///      first for that record wins. This is the precise, load-bearing meaning of
///      the instruction's own qualifier -- "when there are no genuine conflicts"
///      -- demonstrated as a positive, mechanistically-explained fact rather than
///      assumed to just work.
/// </summary>
public class SourceOrderInvarianceTests
{
    private static readonly string ConflictFreeDir = Path.Combine(AppContext.BaseDirectory, "validation", "fixtures", "source-order-invariance", "conflict-free");
    private static readonly string Task007Dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tasks", "task-007-multi-source-mule-network", "environment", "data");

    private static CaseSourceDefinition Csv(string dir) => new("csv", "a", Path: Path.Combine(dir, "source_a.csv"));
    private static CaseSourceDefinition Json(string dir) => new("json", "b", Path: Path.Combine(dir, "source_b.json"));
    private static CaseSourceDefinition Parquet(string dir) => new("parquet", "c", Path: Path.Combine(dir, "source_c.parquet"));
    private static CaseSourceDefinition Graphml(string dir) => new("graphml", "d", Path: Path.Combine(dir, "source_d.graphml"));

    private static async Task<string> HashForOrder(IReadOnlyList<CaseSourceDefinition> sources)
    {
        var def = new CaseDefinition("order-invariance-test", sources);
        var result = await CaseLoader.LoadAsync(def, AdapterRegistry.CreateDefault());
        Assert.True(result.AllSourcesLoaded, string.Join("; ", result.Failures.Select(f => f.ErrorMessage)));
        return CanonicalHashing.ComputeCaseHash(result.MergedCase);
    }

    [Fact]
    public async Task ConflictFreeCase_ABCD_DCBA_BDAC_AllProduceIdenticalHash()
    {
        var a = Csv(ConflictFreeDir);
        var b = Json(ConflictFreeDir);
        var c = Parquet(ConflictFreeDir);
        var d = Graphml(ConflictFreeDir);

        var hashAbcd = await HashForOrder(new[] { a, b, c, d });
        var hashDcba = await HashForOrder(new[] { d, c, b, a });
        var hashBdac = await HashForOrder(new[] { b, d, a, c });

        Assert.Equal(hashAbcd, hashDcba);
        Assert.Equal(hashAbcd, hashBdac);
    }

    [Fact]
    public async Task ConflictFreeCase_AllRecordsPresentRegardlessOfOrder()
    {
        var a = Csv(ConflictFreeDir);
        var b = Json(ConflictFreeDir);
        var c = Parquet(ConflictFreeDir);
        var d = Graphml(ConflictFreeDir);

        var def1 = new CaseDefinition("x", new[] { a, b, c, d });
        var def2 = new CaseDefinition("x", new[] { d, c, b, a });

        var result1 = await CaseLoader.LoadAsync(def1, AdapterRegistry.CreateDefault());
        var result2 = await CaseLoader.LoadAsync(def2, AdapterRegistry.CreateDefault());

        Assert.Equal(5, result1.MergedCase.Transactions.Count); // TA-001/002, TB-001/002, TC-001
        Assert.Equal(5, result2.MergedCase.Transactions.Count);
        Assert.Empty(result1.MergedCase.Conflicts);
        Assert.Empty(result2.MergedCase.Conflicts);
    }

    private static CaseSourceDefinition Task007Csv() => new("csv", "primary", Path: Path.Combine(Task007Dir, "transactions_primary.csv"));
    private static CaseSourceDefinition Task007Json() => new("json", "correspondent", Path: Path.Combine(Task007Dir, "transactions_correspondent.json"));
    private static CaseSourceDefinition Task007Parquet() => new("parquet", "archive", Path: Path.Combine(Task007Dir, "transactions_archive.parquet"));
    private static CaseSourceDefinition Task007Graphml() => new("graphml", "relationships", Path: Path.Combine(Task007Dir, "relationships.graphml"));

    [SkippableFact]
    public async Task RealConflictingCase_OrderDeterminesWhichSideOfTheConflictWins()
    {
        Skip.IfNot(Directory.Exists(Task007Dir), $"task-007 fixture data not found at {Task007Dir}");

        var csv = Task007Csv();
        var json = Task007Json();
        var parquet = Task007Parquet();
        var graphml = Task007Graphml();

        // ABCD: csv, json, parquet, graphml -- json (the correspondent feed) is the
        // first source that carries T2-001, so its timestamp should be kept.
        var abcd = await CaseLoader.LoadAsync(new CaseDefinition("x", new[] { csv, json, parquet, graphml }), AdapterRegistry.CreateDefault());
        // DCBA: graphml, parquet, json, csv -- parquet (the archive) is now the
        // first source carrying T2-001, so ITS timestamp should be kept instead.
        var dcba = await CaseLoader.LoadAsync(new CaseDefinition("x", new[] { graphml, parquet, json, csv }), AdapterRegistry.CreateDefault());
        // BDAC: json, graphml, csv, parquet -- json is still first overall, same
        // winner as ABCD, so this should hash identically to ABCD despite a
        // different overall source order.
        var bdac = await CaseLoader.LoadAsync(new CaseDefinition("x", new[] { json, graphml, csv, parquet }), AdapterRegistry.CreateDefault());

        Assert.Single(abcd.MergedCase.Conflicts);
        Assert.Single(dcba.MergedCase.Conflicts);
        Assert.Single(bdac.MergedCase.Conflicts);

        var t2001Abcd = abcd.MergedCase.Transactions.Single(t => t.TransactionId == "T2-001");
        var t2001Dcba = dcba.MergedCase.Transactions.Single(t => t.TransactionId == "T2-001");
        var t2001Bdac = bdac.MergedCase.Transactions.Single(t => t.TransactionId == "T2-001");

        Assert.NotEqual(t2001Abcd.Timestamp, t2001Dcba.Timestamp); // the genuine conflict makes order matter
        Assert.Equal(t2001Abcd.Timestamp, t2001Bdac.Timestamp); // json is first in both -> same winner -> same value

        var hashAbcd = CanonicalHashing.ComputeCaseHash(abcd.MergedCase);
        var hashDcba = CanonicalHashing.ComputeCaseHash(dcba.MergedCase);
        var hashBdac = CanonicalHashing.ComputeCaseHash(bdac.MergedCase);

        Assert.NotEqual(hashAbcd, hashDcba); // the flagged, expected exception to order-invariance
        Assert.Equal(hashAbcd, hashBdac); // same first-seen winner -> same hash, even with a different order overall
    }
}
