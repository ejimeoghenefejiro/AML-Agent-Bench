using System.Text;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Formats;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class CsvDataAdapterTests
{
    private const string SampleCsv =
        "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
        "T1-001,N001,N002,4500,gbp,2026-01-05T09:00:00Z,bank_transfer,GB,0\n" +
        "T2-003,X002,X003,24500.50,GBP,2026-01-12T09:30:00Z,bank_transfer,GB,1\n";

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void LoadFromBytes_ParsesAllTransactions()
    {
        var adapter = new CsvDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleCsv), "data.csv");

        Assert.Equal(2, dataset.Transactions.Count);
        Assert.Equal(CanonicalSchema.Version, dataset.SchemaVersion);
    }

    [Fact]
    public void LoadFromBytes_MapsFieldsCorrectly()
    {
        var adapter = new CsvDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleCsv), "data.csv");
        var t = dataset.Transactions[0];

        Assert.Equal("T1-001", t.TransactionId);
        Assert.Equal("N001", t.SourceAccount);
        Assert.Equal("N002", t.DestinationAccount);
        Assert.Equal(4500m, t.Amount);
        Assert.Equal("GB", t.Jurisdiction);
        Assert.False(t.SarLinked);
    }

    [Fact]
    public void LoadFromBytes_NormalisesCurrencyToUppercase()
    {
        var adapter = new CsvDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleCsv), "data.csv");
        Assert.Equal("GBP", dataset.Transactions[0].Currency); // was "gbp" in source
    }

    [Fact]
    public void LoadFromBytes_PreservesDecimalPrecision()
    {
        var adapter = new CsvDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleCsv), "data.csv");
        Assert.Equal(24500.50m, dataset.Transactions[1].Amount);
    }

    [Fact]
    public void LoadFromBytes_ParsesSarLinkedBoolean()
    {
        var adapter = new CsvDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleCsv), "data.csv");
        Assert.True(dataset.Transactions[1].SarLinked);
    }

    [Fact]
    public void LoadFromBytes_RecordsSourceLineage()
    {
        var adapter = new CsvDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleCsv), "weekly_transfers.csv");
        var lineage = dataset.Transactions[0].SourceLineage;

        Assert.Equal("csv", lineage.SourceType);
        Assert.Equal("weekly_transfers.csv", lineage.SourceName);
        Assert.Equal("T1-001", lineage.SourceRecordId);
        Assert.Equal("csv", lineage.Adapter);
        Assert.Equal(adapter.AdapterVersion, lineage.AdapterVersion);
    }

    [Fact]
    public void LoadFromBytes_MissingRequiredField_ThrowsAdapterNormalisationException()
    {
        var badCsv = "transaction_id,source_account\nT1-001,N001\n"; // missing destination_account, amount, timestamp
        var adapter = new CsvDataAdapter();
        Assert.Throws<AdapterNormalisationException>(() => adapter.LoadFromBytes(Bytes(badCsv), "bad.csv"));
    }

    [Fact]
    public void LoadFromBytes_MalformedAmount_ThrowsAdapterNormalisationException()
    {
        var badCsv = "transaction_id,source_account,destination_account,amount,timestamp\n" +
                     "T1-001,N001,N002,not-a-number,2026-01-05T09:00:00Z\n";
        var adapter = new CsvDataAdapter();
        Assert.Throws<AdapterNormalisationException>(() => adapter.LoadFromBytes(Bytes(badCsv), "bad.csv"));
    }

    [Fact]
    public void LoadFromBytes_DuplicateTransactionId_ThrowsAdapterNormalisationException()
    {
        var dupCsv = "transaction_id,source_account,destination_account,amount,timestamp\n" +
                     "T1-001,N001,N002,100,2026-01-05T09:00:00Z\n" +
                     "T1-001,N003,N004,200,2026-01-06T09:00:00Z\n";
        var adapter = new CsvDataAdapter();
        Assert.Throws<AdapterNormalisationException>(() => adapter.LoadFromBytes(Bytes(dupCsv), "dup.csv"));
    }

    [Fact]
    public void LoadFromBytes_EmptyFile_ThrowsAdapterSourceException()
    {
        var adapter = new CsvDataAdapter();
        Assert.Throws<AdapterSourceException>(() => adapter.LoadFromBytes(Bytes(""), "empty.csv"));
    }

    [Fact]
    public void LoadFromBytes_QuotedFieldWithEmbeddedComma_ParsesCorrectly()
    {
        var csv = "transaction_id,source_account,destination_account,amount,timestamp,channel\n" +
                  "T1-001,N001,N002,100,2026-01-05T09:00:00Z,\"wire, international\"\n";
        var adapter = new CsvDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(csv), "data.csv");
        Assert.Equal("wire, international", dataset.Transactions[0].Channel);
    }

    [Fact]
    public async Task LoadAsync_MissingPath_ThrowsInvalidAdapterConfigurationException()
    {
        var adapter = new CsvDataAdapter();
        var source = new DataSourceConfiguration("csv", Path: null);
        await Assert.ThrowsAsync<InvalidAdapterConfigurationException>(() => adapter.LoadAsync(source));
    }

    [Fact]
    public async Task LoadAsync_FileNotFound_ThrowsAdapterSourceException()
    {
        var adapter = new CsvDataAdapter();
        var source = new DataSourceConfiguration("csv", Path: "does/not/exist.csv");
        await Assert.ThrowsAsync<AdapterSourceException>(() => adapter.LoadAsync(source));
    }

    [Fact]
    public void SameBytes_ProduceIdenticalTransactions_Deterministically()
    {
        var adapter = new CsvDataAdapter();
        var d1 = adapter.LoadFromBytes(Bytes(SampleCsv), "data.csv");
        var d2 = adapter.LoadFromBytes(Bytes(SampleCsv), "data.csv");
        Assert.Equal(d1.Transactions, d2.Transactions);
    }
}
