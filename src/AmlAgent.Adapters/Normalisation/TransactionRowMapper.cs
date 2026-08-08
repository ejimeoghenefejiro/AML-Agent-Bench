using System.Globalization;
using AmlAgent.Adapters.Canonical;

namespace AmlAgent.Adapters.Normalisation;

/// <summary>
/// Shared normalisation logic for any tabular-row source (CSV, JSON,
/// Parquet, SQL Server, PostgreSQL all produce "a row with named fields" at
/// some point) -- one place that maps a row into a CanonicalTransaction,
/// instead of duplicating field-mapping/parsing/validation across five
/// adapters. Pure function: given a field-lookup delegate, no I/O.
/// </summary>
public static class TransactionRowMapper
{
    /// <summary>
    /// Maps one row to a CanonicalTransaction. Throws
    /// AdapterNormalisationException with row context for a missing
    /// required field or an unparseable value -- never silently drops or
    /// defaults a required field.
    /// </summary>
    public static CanonicalTransaction Map(
        Func<string, string?> field,
        string sourceType,
        string? sourceName,
        string? table,
        string adapterId,
        string adapterVersion,
        int rowIndex)
    {
        var txnId = Require(field, "transaction_id", "txn_id")
            ?? throw new AdapterNormalisationException(adapterId, $"row {rowIndex}: missing required field transaction_id/txn_id");

        var sourceAccount = Require(field, "source_account", "from_account")
            ?? throw new AdapterNormalisationException(adapterId, $"row {rowIndex} ({txnId}): missing required field source_account");

        var destinationAccount = Require(field, "destination_account", "to_account")
            ?? throw new AdapterNormalisationException(adapterId, $"row {rowIndex} ({txnId}): missing required field destination_account");

        var amountRaw = Require(field, "amount")
            ?? throw new AdapterNormalisationException(adapterId, $"row {rowIndex} ({txnId}): missing required field amount");
        if (!decimal.TryParse(amountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            throw new AdapterNormalisationException(adapterId, $"row {rowIndex} ({txnId}): amount '{amountRaw}' is not a valid decimal");

        var timestampRaw = Require(field, "timestamp")
            ?? throw new AdapterNormalisationException(adapterId, $"row {rowIndex} ({txnId}): missing required field timestamp");
        if (!DateTimeOffset.TryParse(timestampRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            throw new AdapterNormalisationException(adapterId, $"row {rowIndex} ({txnId}): timestamp '{timestampRaw}' could not be parsed");

        var currencyRaw = field("currency");
        string? currency = null;
        if (!string.IsNullOrWhiteSpace(currencyRaw))
        {
            currency = currencyRaw.Trim().ToUpperInvariant();
            if (!IsWellFormedCurrencyCode(currency))
                throw new AdapterNormalisationException(adapterId,
                    $"row {rowIndex} ({txnId}): currency '{currencyRaw}' is not a valid ISO 4217-shaped code (expected 3 letters, e.g. USD)");
        }

        return new CanonicalTransaction(
            TransactionId: txnId,
            SourceAccount: sourceAccount,
            DestinationAccount: destinationAccount,
            Amount: amount,
            Currency: currency,
            Timestamp: timestamp,
            Channel: field("channel"),
            Jurisdiction: field("jurisdiction") ?? field("destination_country"),
            SarLinked: ParseBool(field("sar_linked")),
            SourceLineage: new SourceLineage(sourceType, sourceName, table, txnId, adapterId, adapterVersion));
    }

    /// <summary>Tries each candidate field name in order, returning the first non-empty value.</summary>
    private static string? Require(Func<string, string?> field, params string[] candidates)
    {
        foreach (var name in candidates)
        {
            var value = field(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    /// <summary>
    /// Shape check only (three letters), not a lookup against the actual ISO 4217
    /// registry -- validates the format every real currency code follows without
    /// needing to ship and maintain a full currency-list dependency.
    /// </summary>
    private static bool IsWellFormedCurrencyCode(string code) =>
        code.Length == 3 && code.All(c => c is >= 'A' and <= 'Z');

    private static bool ParseBool(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var t = raw.Trim();
        return t is "1" or "true" or "True" or "TRUE" or "yes" or "Yes" or "YES";
    }
}
