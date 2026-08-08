using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Formats;
using Xunit;

namespace AmlAgent.Tests.Adapters;

/// <summary>
/// Verifies ParquetDataAdapter against real Parquet.Net-written bytes (via
/// ParquetTransactionWriter), not a mock -- a genuine round trip through
/// the actual columnar file format.
/// </summary>
public class ParquetDataAdapterTests
{
    private static CanonicalTransaction SampleTxn(string id, decimal amount, bool sar = false) => new(
        TransactionId: id,
        SourceAccount: "N001",
        DestinationAccount: "N002",
        Amount: amount,
        Currency: "GBP",
        Timestamp: new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
        Channel: "bank_transfer",
        Jurisdiction: "GB",
        SarLinked: sar,
        SourceLineage: new SourceLineage("test", null, null, id, "test", "0"));

    private static async Task<MemoryStream> WriteSampleParquetAsync(IReadOnlyList<CanonicalTransaction> txns)
    {
        var stream = new MemoryStream();
        await ParquetTransactionWriter.WriteAsync(stream, txns);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task RoundTrip_SingleTransaction_PreservesAllFields()
    {
        var original = SampleTxn("T1-001", 4500.75m, sar: true);
        await using var stream = await WriteSampleParquetAsync(new[] { original });

        var adapter = new ParquetDataAdapter();
        var dataset = await adapter.LoadFromStreamAsync(stream, "data.parquet");

        var loaded = Assert.Single(dataset.Transactions);
        Assert.Equal(original.TransactionId, loaded.TransactionId);
        Assert.Equal(original.SourceAccount, loaded.SourceAccount);
        Assert.Equal(original.DestinationAccount, loaded.DestinationAccount);
        Assert.Equal(original.Amount, loaded.Amount);
        Assert.Equal(original.Currency, loaded.Currency);
        Assert.Equal(original.Channel, loaded.Channel);
        Assert.Equal(original.Jurisdiction, loaded.Jurisdiction);
        Assert.Equal(original.SarLinked, loaded.SarLinked);
    }

    [Fact]
    public async Task RoundTrip_MultipleRowGroups_ReadsAllTransactions()
    {
        var txns = Enumerable.Range(1, 5).Select(i => SampleTxn($"T1-{i:000}", i * 100m)).ToList();
        await using var stream = await WriteSampleParquetAsync(txns);

        var adapter = new ParquetDataAdapter();
        var dataset = await adapter.LoadFromStreamAsync(stream, "data.parquet");

        Assert.Equal(5, dataset.Transactions.Count);
        Assert.Equal(new[] { "T1-001", "T1-002", "T1-003", "T1-004", "T1-005" },
            dataset.Transactions.Select(t => t.TransactionId));
    }

    [Fact]
    public async Task RoundTrip_RecordsSourceLineageWithParquetAdapter()
    {
        await using var stream = await WriteSampleParquetAsync(new[] { SampleTxn("T1-001", 100m) });
        var adapter = new ParquetDataAdapter();
        var dataset = await adapter.LoadFromStreamAsync(stream, "transactions-2026.parquet");

        var lineage = dataset.Transactions[0].SourceLineage;
        Assert.Equal("parquet", lineage.SourceType);
        Assert.Equal("transactions-2026.parquet", lineage.SourceName);
        Assert.Equal("parquet", lineage.Adapter);
    }

    [Fact]
    public async Task RoundTrip_DuplicateTransactionId_ThrowsAdapterNormalisationException()
    {
        var txns = new[] { SampleTxn("T1-001", 100m), SampleTxn("T1-001", 200m) };
        await using var stream = await WriteSampleParquetAsync(txns);
        var adapter = new ParquetDataAdapter();

        await Assert.ThrowsAsync<AmlAgent.Adapters.AdapterNormalisationException>(
            () => adapter.LoadFromStreamAsync(stream, "dup.parquet"));
    }

    [Fact]
    public async Task RoundTrip_EmptyDataset_ProducesZeroTransactions()
    {
        await using var stream = await WriteSampleParquetAsync(Array.Empty<CanonicalTransaction>());
        var adapter = new ParquetDataAdapter();
        var dataset = await adapter.LoadFromStreamAsync(stream, "empty.parquet");
        Assert.Empty(dataset.Transactions);
    }

    [Fact]
    public async Task LoadAsync_FileNotFound_ThrowsAdapterSourceException()
    {
        var adapter = new ParquetDataAdapter();
        var source = new AmlAgent.Adapters.DataSourceConfiguration("parquet", Path: "does/not/exist.parquet");
        await Assert.ThrowsAsync<AmlAgent.Adapters.AdapterSourceException>(() => adapter.LoadAsync(source));
    }

    [Fact]
    public async Task CsvAndParquetOfSameData_NormaliseToEquivalentAmountAndCurrency()
    {
        var txn = SampleTxn("T1-001", 4500.75m);
        await using var parquetStream = await WriteSampleParquetAsync(new[] { txn });
        var parquetResult = (await new ParquetDataAdapter().LoadFromStreamAsync(parquetStream, "a.parquet")).Transactions[0];

        const string csv = "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
                            "T1-001,N001,N002,4500.75,GBP,2026-01-05T09:00:00Z,bank_transfer,GB,0\n";
        var csvResult = new CsvDataAdapter().LoadFromBytes(System.Text.Encoding.UTF8.GetBytes(csv), "a.csv").Transactions[0];

        Assert.Equal(csvResult.Amount, parquetResult.Amount);
        Assert.Equal(csvResult.Currency, parquetResult.Currency);
    }
}
