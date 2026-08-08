namespace AmlAgent.Adapters;

/// <summary>
/// Where an adapter should load data from. File-based adapters (CSV, JSON,
/// Parquet, GraphML) use <see cref="Path"/>. Database/API adapters
/// (SQL Server, PostgreSQL, REST, Neo4j) use <see cref="ConnectionProfile"/>
/// (resolved to actual connection details via
/// AmlAgent.Adapters.Configuration.ConnectionProfileResolver -- never a raw
/// connection string or credential here) plus an optional
/// <see cref="Query"/> (SQL query, Cypher query, or REST path).
/// </summary>
public sealed record DataSourceConfiguration(
    string SourceType,
    string? Path = null,
    string? ConnectionProfile = null,
    string? Query = null,
    IReadOnlyDictionary<string, string>? ExtraOptions = null)
{
    public string? Option(string key) =>
        ExtraOptions is not null && ExtraOptions.TryGetValue(key, out var v) ? v : null;
}
