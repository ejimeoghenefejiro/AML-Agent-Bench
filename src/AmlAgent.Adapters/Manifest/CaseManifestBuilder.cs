using System.Text.Json;
using System.Text.Json.Nodes;
using AmlAgent.Adapters.Normalisation;

namespace AmlAgent.Adapters.Manifest;

/// <summary>
/// Builds case_manifest.json for a CaseLoadResult: the case-level provenance
/// record this feeds into the existing benchmark/assurance provenance
/// (CLI-Only spec: case_id, schema_version, sources, source_manifests,
/// merge_conflicts, evidence_integrity, canonical_case_hash,
/// generated_at_utc). load_failures is additional to that shape -- a source
/// that failed to load must never be silently absent from the manifest.
/// </summary>
public static class CaseManifestBuilder
{
    public static JsonObject Build(CaseLoadResult result, DateTimeOffset generatedAtUtc)
    {
        var mergedCase = result.MergedCase;

        return new JsonObject
        {
            ["case_id"] = result.Definition.CaseId,
            ["schema_version"] = mergedCase.SchemaVersion,
            ["sources"] = new JsonArray(result.Definition.Sources.Select(s => (JsonNode)new JsonObject
            {
                ["source_type"] = s.SourceType,
                ["role"] = s.Role,
                ["path"] = s.Path,
                ["connection_profile"] = s.ConnectionProfile, // a profile name, never the resolved secret
                ["query"] = s.Query,
            }).ToArray()),
            ["load_failures"] = new JsonArray(result.Failures.Select(f => (JsonNode)new JsonObject
            {
                ["source_type"] = f.SourceType,
                ["role"] = f.Role,
                ["error"] = f.ErrorMessage,
            }).ToArray()),
            ["source_manifests"] = new JsonArray(mergedCase.SourceManifest.Select(m => (JsonNode)new JsonObject
            {
                ["source_type"] = m.SourceType,
                ["source_name"] = m.SourceName,
                ["adapter"] = m.Adapter,
                ["adapter_version"] = m.AdapterVersion,
                ["record_count"] = m.RecordCount,
                ["dataset_hash"] = m.DatasetHash,
            }).ToArray()),
            ["merge_conflicts"] = new JsonArray(mergedCase.Conflicts.Select(c => (JsonNode)new JsonObject
            {
                ["record_type"] = c.RecordType,
                ["record_id"] = c.RecordId,
                ["conflict_type"] = c.ConflictType,
                ["description"] = c.Description,
            }).ToArray()),
            ["evidence_integrity"] = EvidenceIntegrityJson.ToJson(result.EvidenceIntegrity),
            ["record_counts"] = new JsonObject
            {
                ["transactions"] = mergedCase.Transactions.Count,
                ["accounts"] = mergedCase.Accounts.Count,
                ["customers"] = mergedCase.Customers.Count,
                ["entities"] = mergedCase.Entities.Count,
                ["relationships"] = mergedCase.Relationships.Count,
                ["cases"] = mergedCase.Cases.Count,
                ["alerts"] = mergedCase.Alerts.Count,
                ["evidence"] = mergedCase.Evidence.Count,
                ["jurisdictions"] = mergedCase.Jurisdictions.Count,
                ["sars"] = mergedCase.Sars.Count,
            },
            ["canonical_case_hash"] = CanonicalHashing.ComputeCaseHash(mergedCase),
            ["generated_at_utc"] = generatedAtUtc.ToString("o"),
        };
    }

    public static string Write(JsonObject manifest, string outputPath)
    {
        var serialised = manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, serialised);
        return outputPath;
    }
}
