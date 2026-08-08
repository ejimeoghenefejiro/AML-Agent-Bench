using AmlAgent.Adapters.Canonical;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace AmlAgent.Adapters.Formats;

/// <summary>
/// Writes canonical transactions out as a real Parquet file. Used both as a
/// genuine export capability (round-tripping canonical data back into a
/// columnar format) and to generate real Parquet fixtures for
/// ParquetDataAdapterTests -- the adapter is verified against actual
/// Parquet.Net-written bytes, not a mock.
/// </summary>
public static class ParquetTransactionWriter
{
    public static async Task WriteAsync(Stream stream, IReadOnlyList<CanonicalTransaction> transactions, CancellationToken cancellationToken = default)
    {
        var idField = new DataField<string>("transaction_id");
        var sourceField = new DataField<string>("source_account");
        var destField = new DataField<string>("destination_account");
        var amountField = new DataField<decimal>("amount");
        var currencyField = new DataField<string?>("currency");
        var timestampField = new DataField<DateTime>("timestamp");
        var channelField = new DataField<string?>("channel");
        var jurisdictionField = new DataField<string?>("jurisdiction");
        var sarField = new DataField<bool>("sar_linked");

        var schema = new ParquetSchema(idField, sourceField, destField, amountField, currencyField, timestampField, channelField, jurisdictionField, sarField);

        using var writer = await ParquetWriter.CreateAsync(schema, stream, cancellationToken: cancellationToken);
        using var rowGroup = writer.CreateRowGroup();

        await rowGroup.WriteColumnAsync(new DataColumn(idField, transactions.Select(t => t.TransactionId).ToArray()));
        await rowGroup.WriteColumnAsync(new DataColumn(sourceField, transactions.Select(t => t.SourceAccount).ToArray()));
        await rowGroup.WriteColumnAsync(new DataColumn(destField, transactions.Select(t => t.DestinationAccount).ToArray()));
        await rowGroup.WriteColumnAsync(new DataColumn(amountField, transactions.Select(t => t.Amount).ToArray()));
        await rowGroup.WriteColumnAsync(new DataColumn(currencyField, transactions.Select(t => t.Currency).ToArray()));
        await rowGroup.WriteColumnAsync(new DataColumn(timestampField, transactions.Select(t => t.Timestamp.UtcDateTime).ToArray()));
        await rowGroup.WriteColumnAsync(new DataColumn(channelField, transactions.Select(t => t.Channel).ToArray()));
        await rowGroup.WriteColumnAsync(new DataColumn(jurisdictionField, transactions.Select(t => t.Jurisdiction).ToArray()));
        await rowGroup.WriteColumnAsync(new DataColumn(sarField, transactions.Select(t => t.SarLinked).ToArray()));
    }
}
