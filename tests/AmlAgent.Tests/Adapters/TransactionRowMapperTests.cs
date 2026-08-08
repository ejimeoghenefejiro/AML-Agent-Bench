using AmlAgent.Adapters;
using AmlAgent.Adapters.Normalisation;
using Xunit;

namespace AmlAgent.Tests.Adapters;

/// <summary>
/// Direct tests of the mapper shared by every tabular-row adapter (CSV, JSON,
/// Parquet, PostgreSQL, SQL Server) -- exercised here without any file/DB
/// plumbing, via a plain field-lookup delegate.
/// </summary>
public class TransactionRowMapperTests
{
    private static Func<string, string?> Row(params (string Key, string? Value)[] fields)
    {
        var dict = fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);
        return key => dict.TryGetValue(key, out var v) ? v : null;
    }

    private static readonly (string, string?)[] ValidBase =
    {
        ("transaction_id", "T1"),
        ("source_account", "A1"),
        ("destination_account", "A2"),
        ("amount", "100.00"),
        ("timestamp", "2026-01-19T10:00:00Z"),
    };

    [Fact]
    public void Map_ValidRow_ProducesExpectedTransaction()
    {
        var txn = TransactionRowMapper.Map(Row(ValidBase), "csv", "f.csv", null, "csv", "1.0.0", 1);
        Assert.Equal("T1", txn.TransactionId);
        Assert.Equal("A1", txn.SourceAccount);
        Assert.Equal("A2", txn.DestinationAccount);
        Assert.Equal(100.00m, txn.Amount);
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData("GBP", "GBP")]
    [InlineData(" eur ", "EUR")]
    public void Map_ValidCurrencyCode_NormalisedToUppercase(string raw, string expected)
    {
        var fields = ValidBase.Append(("currency", raw)).ToArray();
        var txn = TransactionRowMapper.Map(Row(fields), "csv", "f.csv", null, "csv", "1.0.0", 1);
        Assert.Equal(expected, txn.Currency);
    }

    [Theory]
    [InlineData("US")]      // too short
    [InlineData("USDX")]    // too long
    [InlineData("US1")]     // contains a digit
    [InlineData("$$$")]     // not letters at all
    public void Map_MalformedCurrencyCode_ThrowsAdapterNormalisationException(string badCurrency)
    {
        var fields = ValidBase.Append(("currency", badCurrency)).ToArray();
        var ex = Assert.Throws<AdapterNormalisationException>(() =>
            TransactionRowMapper.Map(Row(fields), "csv", "f.csv", null, "csv", "1.0.0", 1));
        Assert.Contains("currency", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_MissingCurrency_LeavesCurrencyNull_NotAnError()
    {
        var txn = TransactionRowMapper.Map(Row(ValidBase), "csv", "f.csv", null, "csv", "1.0.0", 1);
        Assert.Null(txn.Currency);
    }

    [Fact]
    public void Map_MissingTransactionId_ThrowsAdapterNormalisationException()
    {
        var fields = ValidBase.Where(f => f.Item1 != "transaction_id").ToArray();
        Assert.Throws<AdapterNormalisationException>(() =>
            TransactionRowMapper.Map(Row(fields), "csv", "f.csv", null, "csv", "1.0.0", 1));
    }

    [Fact]
    public void Map_TransactionIdAliasTxnId_IsAccepted()
    {
        var fields = ValidBase.Where(f => f.Item1 != "transaction_id").Append(("txn_id", "T1")).ToArray();
        var txn = TransactionRowMapper.Map(Row(fields), "csv", "f.csv", null, "csv", "1.0.0", 1);
        Assert.Equal("T1", txn.TransactionId);
    }

    [Fact]
    public void Map_SourceAccountAliasFromAccount_IsAccepted()
    {
        var fields = ValidBase.Where(f => f.Item1 != "source_account").Append(("from_account", "A9")).ToArray();
        var txn = TransactionRowMapper.Map(Row(fields), "csv", "f.csv", null, "csv", "1.0.0", 1);
        Assert.Equal("A9", txn.SourceAccount);
    }

    [Fact]
    public void Map_UnparseableAmount_ThrowsAdapterNormalisationException()
    {
        var fields = ValidBase.Where(f => f.Item1 != "amount").Append(("amount", "not-a-number")).ToArray();
        Assert.Throws<AdapterNormalisationException>(() =>
            TransactionRowMapper.Map(Row(fields), "csv", "f.csv", null, "csv", "1.0.0", 1));
    }

    [Fact]
    public void Map_UnparseableTimestamp_ThrowsAdapterNormalisationException()
    {
        var fields = ValidBase.Where(f => f.Item1 != "timestamp").Append(("timestamp", "not-a-date")).ToArray();
        Assert.Throws<AdapterNormalisationException>(() =>
            TransactionRowMapper.Map(Row(fields), "csv", "f.csv", null, "csv", "1.0.0", 1));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void Map_SarLinkedBooleanParsing(string? raw, bool expected)
    {
        var fields = ValidBase.Append(("sar_linked", raw)).ToArray();
        var txn = TransactionRowMapper.Map(Row(fields), "csv", "f.csv", null, "csv", "1.0.0", 1);
        Assert.Equal(expected, txn.SarLinked);
    }

    [Fact]
    public void Map_RecordsSourceLineageFields()
    {
        var txn = TransactionRowMapper.Map(Row(ValidBase), "sqlserver", "mydb", "transactions", "sqlserver", "1.0.0", 3);
        Assert.Equal("sqlserver", txn.SourceLineage.SourceType);
        Assert.Equal("mydb", txn.SourceLineage.SourceName);
        Assert.Equal("transactions", txn.SourceLineage.Table);
        Assert.Equal("T1", txn.SourceLineage.SourceRecordId);
        Assert.Equal("sqlserver", txn.SourceLineage.Adapter);
        Assert.Equal("1.0.0", txn.SourceLineage.AdapterVersion);
    }

    [Fact]
    public void Map_SameRowMappedTwice_IsDeterministic()
    {
        var t1 = TransactionRowMapper.Map(Row(ValidBase), "csv", "f.csv", null, "csv", "1.0.0", 1);
        var t2 = TransactionRowMapper.Map(Row(ValidBase), "csv", "f.csv", null, "csv", "1.0.0", 1);
        Assert.Equal(t1, t2);
    }
}
