using System.Globalization;
using System.Text.Json.Nodes;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Normalisation;

namespace AmlAgent.Adapters.Formats;

/// <summary>
/// Shared JSON-array-of-transactions parsing, used by both JsonDataAdapter
/// (reading a local file) and RestApiDataAdapter (reading an HTTP response
/// body) -- same parsing and normalisation, different source lineage.
/// </summary>
internal static class JsonRecordParser
{
    public static CanonicalAmlDataset ParseTransactions(string jsonText, string sourceType, string? sourceName, string adapterId, string adapterVersion)
    {
        var records = ParseJsonArray(jsonText, adapterId);
        return MapRecords(records, sourceType, sourceName, adapterId, adapterVersion);
    }

    public static CanonicalAmlDataset ParseJsonlTransactions(string jsonlText, string sourceType, string? sourceName, string adapterId, string adapterVersion)
    {
        var records = ParseJsonl(jsonlText, adapterId);
        return MapRecords(records, sourceType, sourceName, adapterId, adapterVersion);
    }

    private static CanonicalAmlDataset MapRecords(List<JsonObject> records, string sourceType, string? sourceName, string adapterId, string adapterVersion)
    {
        var transactions = new List<CanonicalTransaction>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < records.Count; i++)
        {
            var rec = records[i];
            string? Field(string name) => NodeToFieldString(rec[name]);

            var txn = TransactionRowMapper.Map(Field, sourceType, sourceName, null, adapterId, adapterVersion, i + 1);
            if (!seenIds.Add(txn.TransactionId))
                throw new AdapterNormalisationException(adapterId, $"record {i + 1}: duplicate transaction_id '{txn.TransactionId}'");
            transactions.Add(txn);
        }

        return CanonicalAmlDataset.Empty() with { Transactions = transactions };
    }

    public static List<JsonObject> ParseJsonl(string text, string adapterId)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var records = new List<JsonObject>();
        for (int i = 0; i < lines.Count; i++)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(lines[i]); }
            catch (Exception ex) { throw new AdapterSourceException(adapterId, $"line {i + 1}: invalid JSON ({ex.Message})"); }

            if (node is not JsonObject obj)
                throw new AdapterSourceException(adapterId, $"line {i + 1}: expected a JSON object");
            records.Add(obj);
        }
        return records;
    }

    public static List<JsonObject> ParseJsonArray(string text, string adapterId)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(text); }
        catch (Exception ex) { throw new AdapterSourceException(adapterId, $"invalid JSON ({ex.Message})"); }

        var array = root as JsonArray;
        if (array is null && root is JsonObject wrapper)
        {
            foreach (var key in new[] { "transactions", "rows", "data", "records" })
                if (wrapper[key] is JsonArray candidate) { array = candidate; break; }
        }
        if (array is null)
            throw new AdapterSourceException(adapterId,
                "expected a top-level JSON array, or an object wrapping one under transactions/rows/data/records");

        var records = new List<JsonObject>();
        foreach (var item in array)
        {
            if (item is not JsonObject obj)
                throw new AdapterSourceException(adapterId, "an array element is not a JSON object");
            records.Add(obj);
        }
        return records;
    }

    public static string? NodeToFieldString(JsonNode? node)
    {
        if (node is not JsonValue value) return node?.ToJsonString();
        if (value.TryGetValue<string>(out var s)) return s;
        if (value.TryGetValue<bool>(out var b)) return b.ToString();
        if (value.TryGetValue<double>(out var d)) return d.ToString(CultureInfo.InvariantCulture);
        return value.ToJsonString();
    }
}
