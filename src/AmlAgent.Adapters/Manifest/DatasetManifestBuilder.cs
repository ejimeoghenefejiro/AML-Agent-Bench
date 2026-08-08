using System.Text.Json;
using System.Text.Json.Nodes;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Normalisation;

namespace AmlAgent.Adapters.Manifest;

/// <summary>
/// Builds and writes dataset_manifest.json for one adapter's load of one
/// source: dataset_id, adapter identity/version, schema version, record
/// counts, snapshot timestamp, and the dataset_hash / normalisation_hash
/// pair (CLI-Only spec section 13). Uses JsonObject directly rather than a
/// POCO, matching how AssuranceProfileBuilder/ReportBuilder already write
/// every other output artifact in this codebase.
/// </summary>
public static class DatasetManifestBuilder
{
    /// <summary>
    /// Builds the manifest for a dataset already loaded via adapter.LoadAsync(source).
    /// For file-based sources, dataset_hash is a real hash of the raw file bytes (read
    /// independently here, not by asking the adapter to expose them). For live/queried
    /// sources (database, REST, graph) with no local file, no adapter currently exposes
    /// its raw pre-normalisation bytes, so dataset_hash is left null and the limitation
    /// is recorded explicitly in snapshot_limitation rather than faking a hash --
    /// normalisation_hash (over the actual canonical output) is still real and populated.
    /// </summary>
    public static async Task<JsonObject> BuildAsync(
        DataSourceConfiguration source,
        IAmlDataAdapter adapter,
        CanonicalAmlDataset dataset,
        DateTimeOffset snapshotTimestamp,
        CancellationToken cancellationToken = default)
    {
        string? datasetHash = null;
        string? snapshotLimitation = null;

        if (!string.IsNullOrWhiteSpace(source.Path) && File.Exists(source.Path))
        {
            var rawBytes = await File.ReadAllBytesAsync(source.Path, cancellationToken);
            datasetHash = CanonicalHashing.ComputeDatasetHash(rawBytes);
        }
        else
        {
            snapshotLimitation =
                $"source type '{source.SourceType}' is a live/queried source; its raw pre-normalisation " +
                "snapshot is not captured by this adapter, so dataset_hash is unavailable for this run -- " +
                "normalisation_hash (over the canonical output) is the only integrity check available here. " +
                "Re-running the same query may return different rows if the underlying data changed.";
        }

        var normalisationHash = CanonicalHashing.ComputeNormalisationHash(dataset);
        var sourceName = !string.IsNullOrWhiteSpace(source.Path) ? Path.GetFileName(source.Path) : source.ConnectionProfile;

        return new JsonObject
        {
            ["dataset_id"] = $"{source.SourceType}-{snapshotTimestamp:yyyyMMddTHHmmssZ}",
            ["source_type"] = source.SourceType,
            ["source_name"] = sourceName,
            ["adapter"] = adapter.AdapterId,
            ["adapter_version"] = adapter.AdapterVersion,
            ["schema_version"] = dataset.SchemaVersion,
            ["record_count"] = dataset.TotalRecordCount,
            ["record_counts_by_type"] = new JsonObject
            {
                ["transactions"] = dataset.Transactions.Count,
                ["accounts"] = dataset.Accounts.Count,
                ["customers"] = dataset.Customers.Count,
                ["entities"] = dataset.Entities.Count,
                ["relationships"] = dataset.Relationships.Count,
                ["cases"] = dataset.Cases.Count,
                ["alerts"] = dataset.Alerts.Count,
                ["evidence"] = dataset.Evidence.Count,
                ["jurisdictions"] = dataset.Jurisdictions.Count,
                ["sars"] = dataset.Sars.Count,
            },
            ["snapshot_timestamp"] = snapshotTimestamp.ToString("o"),
            ["dataset_hash"] = datasetHash,
            ["normalisation_hash"] = normalisationHash,
            ["snapshot_limitation"] = snapshotLimitation,
        };
    }

    /// <summary>Writes the manifest as indented JSON to outputPath, returning the path for convenience.</summary>
    public static string Write(JsonObject manifest, string outputPath)
    {
        var serialised = manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, serialised);
        return outputPath;
    }
}
