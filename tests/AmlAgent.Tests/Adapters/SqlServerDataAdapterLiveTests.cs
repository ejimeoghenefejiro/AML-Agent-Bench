using AmlAgent.Adapters;
using AmlAgent.Adapters.Configuration;
using AmlAgent.Adapters.Database;
using Xunit;

namespace AmlAgent.Tests.Adapters;

/// <summary>
/// Genuine end-to-end tests against a real SQL Server instance -- not a
/// mock. Skipped (not failed) when no live database is configured, same
/// SkippableFact pattern as PostgreSqlDataAdapterLiveTests.
///
/// Verified live on 2026-08-08 against
/// mcr.microsoft.com/mssql/server:2022-latest in a temporary Docker
/// container with a hand-seeded `transactions` table (identical fixture
/// data to the PostgreSQL live tests, to cross-check both relational
/// adapters normalise the same data identically).
/// </summary>
public class SqlServerDataAdapterLiveTests
{
    private const string ProfileName = "test-mssql";

    private static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionProfileResolver.EnvVarNameFor(ProfileName)));

    [SkippableFact]
    public async Task LoadAsync_RealSqlServer_LoadsAndNormalisesSeededTransactions()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live SQL Server connection string to run this");

        var adapter = new SqlServerDataAdapter();
        var source = new DataSourceConfiguration("sqlserver", ConnectionProfile: ProfileName);
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
    public async Task LoadAsync_RealSqlServer_RecordsCorrectSourceLineage()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live SQL Server connection string to run this");

        var adapter = new SqlServerDataAdapter();
        var source = new DataSourceConfiguration("sqlserver", ConnectionProfile: ProfileName);
        var dataset = await adapter.LoadAsync(source);

        var lineage = dataset.Transactions[0].SourceLineage;
        Assert.Equal("sqlserver", lineage.SourceType);
        Assert.Equal("transactions", lineage.Table);
        Assert.Equal("sqlserver", lineage.Adapter);
    }

    [SkippableFact]
    public async Task LoadAsync_RealSqlServer_CustomQuery_IsRespected()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live SQL Server connection string to run this");

        var adapter = new SqlServerDataAdapter();
        var source = new DataSourceConfiguration("sqlserver", ConnectionProfile: ProfileName,
            Query: "SELECT transaction_id, source_account, destination_account, amount, currency, [timestamp], channel, jurisdiction, sar_linked FROM transactions WHERE sar_linked = 1");
        var dataset = await adapter.LoadAsync(source);

        Assert.Equal(2, dataset.Transactions.Count); // T2-003 and T3-001
        Assert.All(dataset.Transactions, t => Assert.True(t.SarLinked));
    }

    [SkippableFact]
    public async Task LoadAsync_RealSqlServer_BadConnectionProfile_ThrowsClearError()
    {
        Skip.IfNot(IsConfigured, "requires SqlClient to actually attempt a connection, so still gated on SQL Server being reachable in this environment");

        var adapter = new SqlServerDataAdapter();
        var source = new DataSourceConfiguration("sqlserver", ConnectionProfile: $"nonexistent-{Guid.NewGuid():N}");
        await Assert.ThrowsAsync<InvalidAdapterConfigurationException>(() => adapter.LoadAsync(source));
    }

    [SkippableFact]
    public async Task LoadAsync_PostgresAndSqlServer_NormaliseIdenticalFixtureDataToEquivalentTransactions()
    {
        var pgConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionProfileResolver.EnvVarNameFor("test-pg")));
        Skip.IfNot(IsConfigured && pgConfigured, "requires both AML_CONN_TEST_MSSQL and AML_CONN_TEST_PG to be set to instances seeded with the identical fixture");

        var mssqlTxn = (await new SqlServerDataAdapter().LoadAsync(new DataSourceConfiguration("sqlserver", ConnectionProfile: ProfileName)))
            .Transactions.Single(t => t.TransactionId == "T1-001");
        var pgTxn = (await new PostgreSqlDataAdapter().LoadAsync(new DataSourceConfiguration("postgresql", ConnectionProfile: "test-pg")))
            .Transactions.Single(t => t.TransactionId == "T1-001");

        Assert.Equal(mssqlTxn.Amount, pgTxn.Amount);
        Assert.Equal(mssqlTxn.Currency, pgTxn.Currency);
        Assert.Equal(mssqlTxn.SarLinked, pgTxn.SarLinked);
    }
}
