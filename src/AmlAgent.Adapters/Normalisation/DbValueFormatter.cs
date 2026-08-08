using System.Globalization;

namespace AmlAgent.Adapters.Normalisation;

/// <summary>
/// Converts a typed database column value (as returned by
/// IDataReader.GetValue) into the culture-invariant string
/// TransactionRowMapper expects. A bare `value.ToString()` is NOT safe
/// here: DateTime.ToString() and decimal.ToString() both use the current
/// culture by default, so e.g. a UK-locale machine renders a timestamptz
/// as "19/01/2026 10:00:00" instead of ISO 8601 -- which then fails
/// InvariantCulture parsing downstream. Found via a live PostgreSQL test
/// (not a mock), which is exactly why that test exists.
/// </summary>
public static class DbValueFormatter
{
    public static string? ToFieldString(object? value) => value switch
    {
        null or DBNull => null,
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        decimal dec => dec.ToString(CultureInfo.InvariantCulture),
        double dbl => dbl.ToString(CultureInfo.InvariantCulture),
        float flt => flt.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => value.ToString(),
    };
}
