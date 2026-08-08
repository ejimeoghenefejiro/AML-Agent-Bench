using System.Text.Json.Nodes;

namespace AmlAgent.Adapters;

/// <summary>
/// The case-definition input format for multi-source case loading: a case id
/// plus an ordered list of sources, each with a human-readable role
/// (transactions/customers/relationships/watchlist/...) alongside the same
/// fields DataSourceConfiguration takes. Source order is preserved and
/// matters -- CanonicalCaseMerger keeps the first-seen value on a
/// cross-source conflict, so listing your most-trusted source first is a
/// deliberate part of authoring a case definition, not an implementation
/// detail.
/// </summary>
public sealed record CaseDefinition(string CaseId, IReadOnlyList<CaseSourceDefinition> Sources);

public sealed record CaseSourceDefinition(
    string SourceType,
    string? Role = null,
    string? Path = null,
    string? ConnectionProfile = null,
    string? Query = null,
    IReadOnlyDictionary<string, string>? Options = null)
{
    public DataSourceConfiguration ToDataSourceConfiguration() => new(SourceType, Path, ConnectionProfile, Query, Options);
}

/// <summary>Parses a case-definition JSON document. Never invents a default case_id or source list -- every malformed/missing field is a clear, specific error.</summary>
public static class CaseDefinitionReader
{
    /// <summary>
    /// baseDirectoryForRelativePaths: when set, every source's relative file Path is
    /// resolved against this directory (matching how rubric.json's
    /// gold_evidence_annotations already resolves relative to rubric.json's own
    /// directory) instead of the current process working directory. Left null by
    /// default so existing callers see no behaviour change.
    /// </summary>
    public static CaseDefinition Parse(string json, string? sourcePathForErrors = null, string? baseDirectoryForRelativePaths = null)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            throw new InvalidCaseDefinitionException($"invalid JSON{Suffix(sourcePathForErrors)}: {ex.Message}");
        }

        var obj = root as JsonObject
            ?? throw new InvalidCaseDefinitionException($"case definition{Suffix(sourcePathForErrors)} must be a JSON object");

        var caseId = (string?)obj["case_id"];
        if (string.IsNullOrWhiteSpace(caseId))
            throw new InvalidCaseDefinitionException($"case definition{Suffix(sourcePathForErrors)} is missing required field 'case_id'");

        var sourcesNode = obj["sources"] as JsonArray;
        if (sourcesNode is null || sourcesNode.Count == 0)
            throw new InvalidCaseDefinitionException($"case definition{Suffix(sourcePathForErrors)} must have a non-empty 'sources' array");

        var sources = new List<CaseSourceDefinition>();
        for (int i = 0; i < sourcesNode.Count; i++)
        {
            if (sourcesNode[i] is not JsonObject srcObj)
                throw new InvalidCaseDefinitionException($"sources[{i}]{Suffix(sourcePathForErrors)} must be a JSON object");

            var sourceType = (string?)srcObj["source_type"];
            if (string.IsNullOrWhiteSpace(sourceType))
                throw new InvalidCaseDefinitionException($"sources[{i}]{Suffix(sourcePathForErrors)} is missing required field 'source_type'");

            IReadOnlyDictionary<string, string>? options = null;
            if (srcObj["options"] is JsonObject optionsObj)
                options = optionsObj.ToDictionary(kv => kv.Key, kv => (string?)kv.Value ?? "");

            var path = (string?)srcObj["path"];
            if (baseDirectoryForRelativePaths is not null && path is not null && !Path.IsPathRooted(path))
                path = Path.Combine(baseDirectoryForRelativePaths, path);

            sources.Add(new CaseSourceDefinition(
                SourceType: sourceType,
                Role: (string?)srcObj["role"],
                Path: path,
                ConnectionProfile: (string?)srcObj["connection_profile"],
                Query: (string?)srcObj["query"],
                Options: options));
        }

        return new CaseDefinition(caseId, sources);
    }

    private static string Suffix(string? sourcePath) => sourcePath is null ? "" : $" ({sourcePath})";
}

public sealed class InvalidCaseDefinitionException : Exception
{
    public InvalidCaseDefinitionException(string message) : base(message) { }
}
