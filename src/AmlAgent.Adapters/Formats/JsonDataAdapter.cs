using System.Text;
using AmlAgent.Adapters.Canonical;

namespace AmlAgent.Adapters.Formats;

/// <summary>
/// Loads a transaction ledger from JSON or JSON Lines (.jsonl) into the
/// canonical model. JSON accepts a top-level array, or an object wrapping
/// the array under transactions/rows/data/records. Parsing itself lives in
/// JsonRecordParser, shared with RestApiDataAdapter.
/// </summary>
public sealed class JsonDataAdapter : IAmlDataAdapter
{
    public string AdapterId => "json";
    public string AdapterVersion => "1.0.0";

    public async Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
            throw new InvalidAdapterConfigurationException(AdapterId, "'Path' is required");
        if (!File.Exists(source.Path))
            throw new AdapterSourceException(AdapterId, $"file not found: {source.Path}");

        var rawBytes = await File.ReadAllBytesAsync(source.Path, cancellationToken);
        var isJsonl = source.Path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
        return LoadFromBytes(rawBytes, source.Path, isJsonl);
    }

    /// <summary>Testable without touching disk -- takes raw file bytes directly.</summary>
    public CanonicalAmlDataset LoadFromBytes(byte[] rawBytes, string sourcePathForLineage, bool isJsonl)
    {
        var text = Encoding.UTF8.GetString(rawBytes);
        var sourceName = Path.GetFileName(sourcePathForLineage);
        var sourceType = isJsonl ? "jsonl" : "json";

        return isJsonl
            ? JsonRecordParser.ParseJsonlTransactions(text, sourceType, sourceName, AdapterId, AdapterVersion)
            : JsonRecordParser.ParseTransactions(text, sourceType, sourceName, AdapterId, AdapterVersion);
    }
}
