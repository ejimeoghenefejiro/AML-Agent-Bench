using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Configuration;
using AmlAgent.Adapters.Normalisation;
using Microsoft.Data.SqlClient;

namespace AmlAgent.Adapters.Database;

/// <summary>
/// Loads a transaction ledger from a SQL Server query into the canonical
/// model. Same shape as PostgreSqlDataAdapter -- both reuse
/// TransactionRowMapper and DbValueFormatter, so the normalisation logic
/// (and its culture-invariance fix) is shared, not duplicated.
/// </summary>
public sealed class SqlServerDataAdapter : IAmlDataAdapter
{
    private const string DefaultQuery =
        "SELECT transaction_id, source_account, destination_account, amount, currency, timestamp, channel, jurisdiction, sar_linked FROM transactions";

    public string AdapterId => "sqlserver";
    public string AdapterVersion => "1.0.0";

    public async Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default)
    {
        var connectionString = ConnectionProfileResolver.Resolve(source.ConnectionProfile, AdapterId);
        var query = string.IsNullOrWhiteSpace(source.Query) ? DefaultQuery : source.Query;

        await using var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new AdapterSourceException(AdapterId, $"SQL Server connection failed for profile '{source.ConnectionProfile}': {ex.Message}", ex);
        }

        await using var command = new SqlCommand(query, connection);
        SqlDataReader reader;
        try
        {
            reader = await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new AdapterSourceException(AdapterId, $"SQL Server query failed: {ex.Message}", ex);
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
                    catch (IndexOutOfRangeException) { return null; }
                    return reader.IsDBNull(ordinal) ? null : DbValueFormatter.ToFieldString(reader.GetValue(ordinal));
                }

                var txn = TransactionRowMapper.Map(Field, "sqlserver", source.ConnectionProfile, InferTableName(query), AdapterId, AdapterVersion, rowIndex);
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
