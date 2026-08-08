using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Normalisation;
using Parquet;
using Parquet.Schema;

namespace AmlAgent.Adapters.Formats;

/// <summary>
/// Loads a flat transaction ledger from a Parquet file into the canonical
/// model. Expects columns named the same as CanonicalTransaction's fields
/// (transaction_id/txn_id, source_account, destination_account, amount,
/// currency, timestamp, channel, jurisdiction, sar_linked) -- any column
/// not present is simply treated as absent, same as a missing CSV column.
/// </summary>
public sealed class ParquetDataAdapter : IAmlDataAdapter
{
    public string AdapterId => "parquet";
    public string AdapterVersion => "1.0.0";

    public async Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
            throw new InvalidAdapterConfigurationException(AdapterId, "'Path' is required");
        if (!File.Exists(source.Path))
            throw new AdapterSourceException(AdapterId, $"file not found: {source.Path}");

        await using var stream = File.OpenRead(source.Path);
        return await LoadFromStreamAsync(stream, source.Path, cancellationToken);
    }

    /// <summary>Testable without touching disk -- takes any readable stream (e.g. a MemoryStream built in a test).</summary>
    public async Task<CanonicalAmlDataset> LoadFromStreamAsync(Stream parquetStream, string sourcePathForLineage, CancellationToken cancellationToken = default)
    {
        ParquetReader reader;
        try
        {
            reader = await ParquetReader.CreateAsync(parquetStream, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new AdapterSourceException(AdapterId, $"could not read parquet file: {ex.Message}", ex);
        }

        using (reader)
        {
            var sourceName = Path.GetFileName(sourcePathForLineage);
            var fieldNames = reader.Schema.DataFields.Select(f => f.Name).ToArray();
            var columnsByName = new Dictionary<string, Array>(StringComparer.OrdinalIgnoreCase);

            for (int g = 0; g < reader.RowGroupCount; g++)
            {
                using var rowGroupReader = reader.OpenRowGroupReader(g);
                foreach (var field in reader.Schema.DataFields)
                {
                    var column = await rowGroupReader.ReadColumnAsync(field, cancellationToken);
                    if (columnsByName.TryGetValue(field.Name, out var existing))
                        columnsByName[field.Name] = Concat(existing, column.Data);
                    else
                        columnsByName[field.Name] = column.Data;
                }
            }

            int rowCount = columnsByName.Count == 0 ? 0 : columnsByName.Values.Max(a => a.Length);
            var transactions = new List<CanonicalTransaction>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rowCount; i++)
            {
                int rowIndex = i;
                string? Field(string name)
                {
                    if (!columnsByName.TryGetValue(name, out var col) || rowIndex >= col.Length) return null;
                    return DbValueFormatter.ToFieldString(col.GetValue(rowIndex));
                }

                var txn = TransactionRowMapper.Map(Field, "parquet", sourceName, null, AdapterId, AdapterVersion, i + 1);
                if (!seenIds.Add(txn.TransactionId))
                    throw new AdapterNormalisationException(AdapterId, $"row {i + 1}: duplicate transaction_id '{txn.TransactionId}'");
                transactions.Add(txn);
            }

            return CanonicalAmlDataset.Empty() with { Transactions = transactions };
        }
    }

    private static Array Concat(Array a, Array b)
    {
        var result = Array.CreateInstance(a.GetType().GetElementType()!, a.Length + b.Length);
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
