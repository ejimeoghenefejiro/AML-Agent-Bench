using System.Text;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Formats;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class JsonDataAdapterTests
{
    private const string SampleJsonArray = """
    [
      {"transaction_id":"T1-001","source_account":"N001","destination_account":"N002","amount":4500,"currency":"gbp","timestamp":"2026-01-05T09:00:00Z","sar_linked":false},
      {"transaction_id":"T2-003","source_account":"X002","destination_account":"X003","amount":24500.5,"currency":"GBP","timestamp":"2026-01-12T09:30:00Z","sar_linked":true}
    ]
    """;

    private const string SampleWrappedJson = """
    {"transactions": [
      {"transaction_id":"T1-001","source_account":"N001","destination_account":"N002","amount":100,"timestamp":"2026-01-05T09:00:00Z"}
    ]}
    """;

    private const string SampleJsonl =
        """{"transaction_id":"T1-001","source_account":"N001","destination_account":"N002","amount":100,"timestamp":"2026-01-05T09:00:00Z"}""" + "\n" +
        """{"transaction_id":"T2-003","source_account":"X002","destination_account":"X003","amount":200,"timestamp":"2026-01-12T09:00:00Z","sar_linked":true}""" + "\n";

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void LoadFromBytes_TopLevelArray_ParsesAllTransactions()
    {
        var adapter = new JsonDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleJsonArray), "data.json", isJsonl: false);
        Assert.Equal(2, dataset.Transactions.Count);
    }

    [Fact]
    public void LoadFromBytes_MapsNumericAndBooleanFieldsCorrectly()
    {
        var adapter = new JsonDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleJsonArray), "data.json", isJsonl: false);
        var t = dataset.Transactions[1];
        Assert.Equal(24500.5m, t.Amount);
        Assert.True(t.SarLinked);
        Assert.Equal("GBP", t.Currency); // normalised from "GBP" (already upper) and "gbp" -> "GBP" for the other row
    }

    [Fact]
    public void LoadFromBytes_WrappedUnderTransactionsKey_ParsesCorrectly()
    {
        var adapter = new JsonDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleWrappedJson), "data.json", isJsonl: false);
        Assert.Single(dataset.Transactions);
        Assert.Equal("T1-001", dataset.Transactions[0].TransactionId);
    }

    [Fact]
    public void LoadFromBytes_Jsonl_ParsesOneRecordPerLine()
    {
        var adapter = new JsonDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleJsonl), "data.jsonl", isJsonl: true);
        Assert.Equal(2, dataset.Transactions.Count);
        Assert.True(dataset.Transactions[1].SarLinked);
    }

    [Fact]
    public void LoadFromBytes_RecordsSourceLineageWithCorrectSourceType()
    {
        var adapter = new JsonDataAdapter();
        var jsonDataset = adapter.LoadFromBytes(Bytes(SampleJsonArray), "data.json", isJsonl: false);
        var jsonlDataset = adapter.LoadFromBytes(Bytes(SampleJsonl), "data.jsonl", isJsonl: true);

        Assert.Equal("json", jsonDataset.Transactions[0].SourceLineage.SourceType);
        Assert.Equal("jsonl", jsonlDataset.Transactions[0].SourceLineage.SourceType);
    }

    [Fact]
    public void LoadFromBytes_MalformedJson_ThrowsAdapterSourceException()
    {
        var adapter = new JsonDataAdapter();
        Assert.Throws<AdapterSourceException>(() => adapter.LoadFromBytes(Bytes("{not valid"), "bad.json", isJsonl: false));
    }

    [Fact]
    public void LoadFromBytes_UnrecognisedShape_ThrowsAdapterSourceException()
    {
        var adapter = new JsonDataAdapter();
        Assert.Throws<AdapterSourceException>(() => adapter.LoadFromBytes(Bytes("""{"unexpected": 1}"""), "bad.json", isJsonl: false));
    }

    [Fact]
    public void LoadFromBytes_DuplicateTransactionId_ThrowsAdapterNormalisationException()
    {
        var dup = "[" +
            """{"transaction_id":"T1-001","source_account":"N001","destination_account":"N002","amount":1,"timestamp":"2026-01-05T09:00:00Z"},""" +
            """{"transaction_id":"T1-001","source_account":"N003","destination_account":"N004","amount":2,"timestamp":"2026-01-06T09:00:00Z"}""" +
            "]";
        var adapter = new JsonDataAdapter();
        Assert.Throws<AdapterNormalisationException>(() => adapter.LoadFromBytes(Bytes(dup), "dup.json", isJsonl: false));
    }

    [Fact]
    public void LoadFromBytes_MissingRequiredField_ThrowsAdapterNormalisationException()
    {
        var missing = """[{"transaction_id":"T1-001","source_account":"N001"}]""";
        var adapter = new JsonDataAdapter();
        Assert.Throws<AdapterNormalisationException>(() => adapter.LoadFromBytes(Bytes(missing), "bad.json", isJsonl: false));
    }

    [Fact]
    public void CsvAndJsonOfSameData_NormaliseToEquivalentTransactions()
    {
        const string csv = "transaction_id,source_account,destination_account,amount,currency,timestamp,sar_linked\n" +
                            "T1-001,N001,N002,4500,GBP,2026-01-05T09:00:00Z,0\n";
        const string json = """[{"transaction_id":"T1-001","source_account":"N001","destination_account":"N002","amount":4500,"currency":"GBP","timestamp":"2026-01-05T09:00:00Z","sar_linked":false}]""";

        var csvTxn = new CsvDataAdapter().LoadFromBytes(Bytes(csv), "a.csv").Transactions[0];
        var jsonTxn = new JsonDataAdapter().LoadFromBytes(Bytes(json), "a.json", isJsonl: false).Transactions[0];

        Assert.Equal(csvTxn.TransactionId, jsonTxn.TransactionId);
        Assert.Equal(csvTxn.SourceAccount, jsonTxn.SourceAccount);
        Assert.Equal(csvTxn.DestinationAccount, jsonTxn.DestinationAccount);
        Assert.Equal(csvTxn.Amount, jsonTxn.Amount);
        Assert.Equal(csvTxn.Currency, jsonTxn.Currency);
        Assert.Equal(csvTxn.Timestamp, jsonTxn.Timestamp);
        Assert.Equal(csvTxn.SarLinked, jsonTxn.SarLinked);
    }
}
