using AmlAgent.Adapters;
using AmlAgent.Adapters.Configuration;
using AmlAgent.Adapters.Database;
using Xunit;

namespace AmlAgent.Tests.Adapters;

/// <summary>
/// Genuine end-to-end tests against a real PostgreSQL instance -- not a
/// mock. Skipped (not failed) when no live database is configured, so the
/// suite stays portable: set AML_CONN_TEST_PG (see
/// ConnectionProfileResolver.EnvVarNameFor("test-pg")) to a real
/// PostgreSQL connection string to exercise these. Same SkippableFact
/// pattern this repo already uses for OPENAI_API_KEY-dependent tests
/// (see JudgeReportTests) -- gracefully absent, not silently faked.
///
/// Verified live on 2026-08-08 against postgres:16-alpine in a temporary
/// Docker container with a hand-seeded `transactions` table.
/// </summary>
public class PostgreSqlDataAdapterLiveTests
{
    private const string ProfileName = "test-pg";

    private static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionProfileResolver.EnvVarNameFor(ProfileName)));

    [SkippableFact]
    public async Task LoadAsync_RealPostgres_LoadsAndNormalisesSeededTransactions()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live PostgreSQL connection string to run this");

        var adapter = new PostgreSqlDataAdapter();
        var source = new DataSourceConfiguration("postgresql", ConnectionProfile: ProfileName);
        var dataset = await adapter.LoadAsync(source);

        Assert.Equal(3, dataset.Transactions.Count);
        var t1 = dataset.Transactions.Single(t => t.TransactionId == "T1-001");
        Assert.Equal("N001", t1.SourceAccount);
        Assert.Equal("N002", t1.DestinationAccount);
        Assert.Equal(4500.00m, t1.Amount);
        Assert.Equal("GBP", t1.Currency);
        Assert.False(t1.SarLinked);

        var t3 = dataset.Transactions.Single(t => t.TransactionId == "T3-001");
        Assert.True(t3.SarLinked);
        Assert.Equal("AE", t3.Jurisdiction);
    }

    [SkippableFact]
    public async Task LoadAsync_RealPostgres_RecordsCorrectSourceLineage()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live PostgreSQL connection string to run this");

        var adapter = new PostgreSqlDataAdapter();
        var source = new DataSourceConfiguration("postgresql", ConnectionProfile: ProfileName);
        var dataset = await adapter.LoadAsync(source);

        var lineage = dataset.Transactions[0].SourceLineage;
        Assert.Equal("postgresql", lineage.SourceType);
        Assert.Equal("transactions", lineage.Table);
        Assert.Equal("postgresql", lineage.Adapter);
    }

    [SkippableFact]
    public async Task LoadAsync_RealPostgres_CustomQuery_IsRespected()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live PostgreSQL connection string to run this");

        var adapter = new PostgreSqlDataAdapter();
        var source = new DataSourceConfiguration("postgresql", ConnectionProfile: ProfileName,
            Query: "SELECT transaction_id, source_account, destination_account, amount, currency, timestamp, channel, jurisdiction, sar_linked FROM transactions WHERE sar_linked = true");
        var dataset = await adapter.LoadAsync(source);

        Assert.Equal(2, dataset.Transactions.Count); // T2-003 and T3-001 in the seeded fixture
        Assert.All(dataset.Transactions, t => Assert.True(t.SarLinked));
    }

    [SkippableFact]
    public async Task LoadAsync_RealPostgres_BadConnectionProfile_ThrowsClearError()
    {
        Skip.IfNot(IsConfigured, "requires Npgsql to actually attempt a connection, so still gated on Postgres being reachable in this environment");

        var adapter = new PostgreSqlDataAdapter();
        // A profile name whose env var isn't set at all.
        var source = new DataSourceConfiguration("postgresql", ConnectionProfile: $"nonexistent-{Guid.NewGuid():N}");
        await Assert.ThrowsAsync<InvalidAdapterConfigurationException>(() => adapter.LoadAsync(source));
    }
}
