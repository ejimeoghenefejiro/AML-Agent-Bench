using System.Text;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Normalisation;

namespace AmlAgent.Adapters.Formats;

/// <summary>Loads a flat transaction ledger from a CSV file into the canonical model.</summary>
public sealed class CsvDataAdapter : IAmlDataAdapter
{
    public string AdapterId => "csv";
    public string AdapterVersion => "1.0.0";

    public async Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
            throw new InvalidAdapterConfigurationException(AdapterId, "'Path' is required");
        if (!File.Exists(source.Path))
            throw new AdapterSourceException(AdapterId, $"file not found: {source.Path}");

        var rawBytes = await File.ReadAllBytesAsync(source.Path, cancellationToken);
        return LoadFromBytes(rawBytes, source.Path);
    }

    /// <summary>Testable without touching disk -- takes raw file bytes directly.</summary>
    public CanonicalAmlDataset LoadFromBytes(byte[] rawBytes, string sourcePathForLineage)
    {
        var text = Encoding.UTF8.GetString(rawBytes);
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            throw new AdapterSourceException(AdapterId, "file is empty");

        var header = CsvLineParser.ParseLine(lines[0]);
        var sourceName = Path.GetFileName(sourcePathForLineage);
        var transactions = new List<CanonicalTransaction>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Count; i++)
        {
            var cells = CsvLineParser.ParseLine(lines[i]);

            string? Field(string name)
            {
                var idx = Array.IndexOf(header, name);
                return idx >= 0 && idx < cells.Length ? cells[idx] : null;
            }

            var txn = TransactionRowMapper.Map(Field, "csv", sourceName, null, AdapterId, AdapterVersion, i);
            if (!seenIds.Add(txn.TransactionId))
                throw new AdapterNormalisationException(AdapterId, $"row {i}: duplicate transaction_id '{txn.TransactionId}'");
            transactions.Add(txn);
        }

        return CanonicalAmlDataset.Empty() with { Transactions = transactions };
    }
}
