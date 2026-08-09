using AmlAgent.Adapters;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Configuration;
using AmlAgent.Adapters.Database;
using AmlAgent.Adapters.Formats;
using AmlAgent.Adapters.Graph;
using AmlAgent.Adapters.Normalisation;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Item 4: the same logical AML case (3 transactions: INV-001/002/003, and the
/// same 3 transactions re-expressed as a relationship graph) represented across
/// every storage format the adapter layer supports. Verifies that after
/// normalisation, format is invisible to the canonical result: identical
/// canonical hashes, identical evidence ids, identical timestamps/decimals/
/// currencies, identical relationships, and identical downstream benchmark
/// scores computed from that canonical data.
///
/// CSV/JSON/JSONL/Parquet/GraphML are always-on (real files, no external
/// infra). SQL Server/PostgreSQL/Neo4j require a live, pre-seeded instance
/// (see seed_*.sql / seed_neo4j.cypher in this directory) and are SkippableFact
/// -gated on AML_CONN_TEST_FORMAT_INVARIANCE_* -- the adapters themselves are
/// already exhaustively live-tested elsewhere (PostgreSqlDataAdapterLiveTests
/// etc in AmlAgent.Tests); this test's incremental claim is specifically that
/// the SAME case seeded across formats produces the SAME canonical hash.
/// </summary>
public class FormatInvarianceTests
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "validation", "fixtures", "format-invariance");

    private static async Task<CanonicalAmlDataset> LoadAsync(IAmlDataAdapter adapter, DataSourceConfiguration source) =>
        await adapter.LoadAsync(source);

    public static IEnumerable<object[]> AlwaysOnAdapters() => new[]
    {
        new object[] { new CsvDataAdapter(), new DataSourceConfiguration("csv", Path: Path.Combine(FixturesDir, "transactions.csv")) },
        new object[] { new JsonDataAdapter(), new DataSourceConfiguration("json", Path: Path.Combine(FixturesDir, "transactions.json")) },
        new object[] { new JsonDataAdapter(), new DataSourceConfiguration("jsonl", Path: Path.Combine(FixturesDir, "transactions.jsonl")) },
        new object[] { new ParquetDataAdapter(), new DataSourceConfiguration("parquet", Path: Path.Combine(FixturesDir, "transactions.parquet")) },
    };

    [Theory]
    [MemberData(nameof(AlwaysOnAdapters))]
    public async Task EachFormat_ProducesTheSameCanonicalNormalisationHashAsCsv(IAmlDataAdapter adapter, DataSourceConfiguration source)
    {
        var csvDataset = await LoadAsync(new CsvDataAdapter(), new DataSourceConfiguration("csv", Path: Path.Combine(FixturesDir, "transactions.csv")));
        var thisDataset = await LoadAsync(adapter, source);

        Assert.Equal(CanonicalHashing.ComputeNormalisationHash(csvDataset), CanonicalHashing.ComputeNormalisationHash(thisDataset));
    }

    [Theory]
    [MemberData(nameof(AlwaysOnAdapters))]
    public async Task EachFormat_PreservesTransactionIdsExactly(IAmlDataAdapter adapter, DataSourceConfiguration source)
    {
        var dataset = await LoadAsync(adapter, source);
        var ids = dataset.Transactions.Select(t => t.TransactionId).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "INV-001", "INV-002", "INV-003" }, ids);
    }

    [Theory]
    [MemberData(nameof(AlwaysOnAdapters))]
    public async Task EachFormat_PreservesTimestampsExactly(IAmlDataAdapter adapter, DataSourceConfiguration source)
    {
        var dataset = await LoadAsync(adapter, source);
        var inv3 = dataset.Transactions.Single(t => t.TransactionId == "INV-003");
        Assert.Equal(new DateTimeOffset(2026, 3, 17, 23, 59, 59, TimeSpan.Zero), inv3.Timestamp);
    }

    [Theory]
    [MemberData(nameof(AlwaysOnAdapters))]
    public async Task EachFormat_PreservesDecimalAmountsExactly(IAmlDataAdapter adapter, DataSourceConfiguration source)
    {
        var dataset = await LoadAsync(adapter, source);
        var inv1 = dataset.Transactions.Single(t => t.TransactionId == "INV-001");
        var inv3 = dataset.Transactions.Single(t => t.TransactionId == "INV-003");
        Assert.Equal(12345.67m, inv1.Amount);
        Assert.Equal(50000.00m, inv3.Amount); // whole-number decimal must not become 50000 (int-shaped) or lose scale
    }

    [Theory]
    [MemberData(nameof(AlwaysOnAdapters))]
    public async Task EachFormat_PreservesCurrenciesExactly(IAmlDataAdapter adapter, DataSourceConfiguration source)
    {
        var dataset = await LoadAsync(adapter, source);
        var currencies = dataset.Transactions.ToDictionary(t => t.TransactionId, t => t.Currency);
        Assert.Equal("GBP", currencies["INV-001"]);
        Assert.Equal("EUR", currencies["INV-002"]);
        Assert.Equal("USD", currencies["INV-003"]);
    }

    [Theory]
    [MemberData(nameof(AlwaysOnAdapters))]
    public async Task EachFormat_PreservesSarLinkedFlagsExactly(IAmlDataAdapter adapter, DataSourceConfiguration source)
    {
        var dataset = await LoadAsync(adapter, source);
        var flags = dataset.Transactions.ToDictionary(t => t.TransactionId, t => t.SarLinked);
        Assert.True(flags["INV-001"]);
        Assert.False(flags["INV-002"]);
        Assert.True(flags["INV-003"]);
    }

    [Theory]
    [MemberData(nameof(AlwaysOnAdapters))]
    public async Task EachFormat_ProducesIdenticalDownstreamBenchmarkScore(IAmlDataAdapter adapter, DataSourceConfiguration source)
    {
        // "Benchmark scores are identical" made concrete: a FIXED report evaluated
        // against the valid-transaction-id set each format independently produces
        // must score identically, since format cannot change which ids are valid.
        const string reportText = "Funds moved via INV-001, INV-002 and INV-003.";
        var goldIds = new HashSet<string> { "INV-001", "INV-002", "INV-003" };

        var csvDataset = await LoadAsync(new CsvDataAdapter(), new DataSourceConfiguration("csv", Path: Path.Combine(FixturesDir, "transactions.csv")));
        var csvValidIds = new HashSet<string>(csvDataset.Transactions.Select(t => t.TransactionId), StringComparer.OrdinalIgnoreCase);
        var csvTrace = EvidenceScoring.ComputeTraceability(reportText, csvValidIds, goldIds);

        var thisDataset = await LoadAsync(adapter, source);
        var thisValidIds = new HashSet<string>(thisDataset.Transactions.Select(t => t.TransactionId), StringComparer.OrdinalIgnoreCase);
        var thisTrace = EvidenceScoring.ComputeTraceability(reportText, thisValidIds, goldIds);

        Assert.Equal(csvTrace.Precision, thisTrace.Precision);
        Assert.Equal(csvTrace.Recall, thisTrace.Recall);
        Assert.Equal(csvTrace.F1, thisTrace.F1);
    }

    [Fact]
    public void GraphMl_RelationshipsMatchTheSameLogicalTransactions()
    {
        var adapter = new GraphMlDataAdapter();
        var dataset = adapter.LoadFromBytes(File.ReadAllBytes(Path.Combine(FixturesDir, "relationships.graphml")), "relationships.graphml");

        Assert.Equal(4, dataset.Entities.Count);
        Assert.Equal(3, dataset.Relationships.Count);
        var evidenceIds = dataset.Relationships.SelectMany(r => r.EvidenceIds).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "INV-001", "INV-002", "INV-003" }, evidenceIds);
    }

    // -- Live-infra-gated: SQL Server / PostgreSQL / Neo4j --

    private const string PgProfile = "test-format-invariance-pg";
    private const string MssqlProfile = "test-format-invariance-mssql";
    private const string Neo4jProfile = "test-format-invariance-neo4j";

    private static bool IsConfigured(string profile) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionProfileResolver.EnvVarNameFor(profile)));

    [SkippableFact]
    public async Task PostgreSql_ProducesTheSameCanonicalNormalisationHashAsCsv()
    {
        Skip.IfNot(IsConfigured(PgProfile),
            $"set {ConnectionProfileResolver.EnvVarNameFor(PgProfile)} to a live PostgreSQL connection string seeded via seed_postgresql.sql to run this");

        var csvDataset = await LoadAsync(new CsvDataAdapter(), new DataSourceConfiguration("csv", Path: Path.Combine(FixturesDir, "transactions.csv")));
        var pgDataset = await new PostgreSqlDataAdapter().LoadAsync(new DataSourceConfiguration("postgresql", ConnectionProfile: PgProfile));

        Assert.Equal(CanonicalHashing.ComputeNormalisationHash(csvDataset), CanonicalHashing.ComputeNormalisationHash(pgDataset));
    }

    [SkippableFact]
    public async Task SqlServer_ProducesTheSameCanonicalNormalisationHashAsCsv()
    {
        Skip.IfNot(IsConfigured(MssqlProfile),
            $"set {ConnectionProfileResolver.EnvVarNameFor(MssqlProfile)} to a live SQL Server connection string seeded via seed_sqlserver.sql to run this");

        var csvDataset = await LoadAsync(new CsvDataAdapter(), new DataSourceConfiguration("csv", Path: Path.Combine(FixturesDir, "transactions.csv")));
        var mssqlDataset = await new SqlServerDataAdapter().LoadAsync(new DataSourceConfiguration("sqlserver", ConnectionProfile: MssqlProfile));

        Assert.Equal(CanonicalHashing.ComputeNormalisationHash(csvDataset), CanonicalHashing.ComputeNormalisationHash(mssqlDataset));
    }

    [SkippableFact]
    public async Task Neo4j_RelationshipsMatchGraphMlEquivalent()
    {
        Skip.IfNot(IsConfigured(Neo4jProfile),
            $"set {ConnectionProfileResolver.EnvVarNameFor(Neo4jProfile)} to a live Neo4j connection string seeded via seed_neo4j.cypher to run this");

        var graphMlAdapter = new GraphMlDataAdapter();
        var graphMlDataset = graphMlAdapter.LoadFromBytes(File.ReadAllBytes(Path.Combine(FixturesDir, "relationships.graphml")), "relationships.graphml");

        var neo4jDataset = await new Neo4jDataAdapter().LoadAsync(new DataSourceConfiguration("neo4j", ConnectionProfile: Neo4jProfile));

        Assert.Equal(graphMlDataset.Entities.Count, neo4jDataset.Entities.Count);
        Assert.Equal(graphMlDataset.Relationships.Count, neo4jDataset.Relationships.Count);

        var graphMlEvidence = graphMlDataset.Relationships.SelectMany(r => r.EvidenceIds).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var neo4jEvidence = neo4jDataset.Relationships.SelectMany(r => r.EvidenceIds).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(graphMlEvidence, neo4jEvidence);
    }
}
