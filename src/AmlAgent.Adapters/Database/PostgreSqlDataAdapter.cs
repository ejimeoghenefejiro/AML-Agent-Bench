using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Configuration;
using AmlAgent.Adapters.Normalisation;
using Npgsql;

namespace AmlAgent.Adapters.Database;

/// <summary>
/// Loads a transaction ledger from a PostgreSQL query into the canonical
/// model. Reuses AmlAgent.Adapters.Normalisation.TransactionRowMapper --
/// the same normalisation logic already exercised by the CSV/JSON/Parquet
/// adapter tests -- so this adapter only needs to prove the connection and
/// query plumbing works, via a field-lookup delegate backed directly by
/// NpgsqlDataReader.
/// </summary>
public sealed class PostgreSqlDataAdapter : IAmlDataAdapter
{
    private const string DefaultQuery =
        "SELECT transaction_id, source_account, destination_account, amount, currency, timestamp, channel, jurisdiction, sar_linked FROM transactions";

    public string AdapterId => "postgresql";
    public string AdapterVersion => "1.0.0";

    public async Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default)
    {
        var connectionString = ConnectionProfileResolver.Resolve(source.ConnectionProfile, AdapterId);
        var query = string.IsNullOrWhiteSpace(source.Query) ? DefaultQuery : source.Query;

        await using var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never include the connection string (may carry credentials) in the error.
            throw new AdapterSourceException(AdapterId, $"PostgreSQL connection failed for profile '{source.ConnectionProfile}': {ex.Message}", ex);
        }

        await using var command = new NpgsqlCommand(query, connection);
        NpgsqlDataReader reader;
        try
        {
            reader = await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new AdapterSourceException(AdapterId, $"PostgreSQL query failed: {ex.Message}", ex);
        }

        await using (reader)
        {
            var transactions = new List<CanonicalTransaction>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int rowIndex = 0;

            while (await reader.ReadAsync(cancellationToken))
            {
                rowIndex++;
                string? Field(string name)
                {
                    int ordinal;
                    try { ordinal = reader.GetOrdinal(name); }
                    catch (IndexOutOfRangeException) { return null; } // column not present in this query
                    return reader.IsDBNull(ordinal) ? null : DbValueFormatter.ToFieldString(reader.GetValue(ordinal));
                }

                var txn = TransactionRowMapper.Map(Field, "postgresql", source.ConnectionProfile, InferTableName(query), AdapterId, AdapterVersion, rowIndex);
                if (!seenIds.Add(txn.TransactionId))
                    throw new AdapterNormalisationException(AdapterId, $"row {rowIndex}: duplicate transaction_id '{txn.TransactionId}'");
                transactions.Add(txn);
            }

            return CanonicalAmlDataset.Empty() with { Transactions = transactions };
        }
    }

    private static string? InferTableName(string query)
    {
        var idx = query.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = query[(idx + 6)..].TrimStart();
        var end = rest.IndexOfAny(new[] { ' ', '\n', '\r', '\t', ';' });
        return end >= 0 ? rest[..end] : rest;
    }
}
