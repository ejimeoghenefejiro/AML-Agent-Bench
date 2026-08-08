using AmlAgent.Adapters.Database;
using AmlAgent.Adapters.Formats;
using AmlAgent.Adapters.Graph;
using AmlAgent.Adapters.Web;

namespace AmlAgent.Adapters;

/// <summary>
/// Maps a source-type string (as it appears in a task's DataSourceConfiguration,
/// e.g. "csv", "postgresql", "neo4j") to the adapter that handles it. This is the
/// single place the Harness/CLI asks "which adapter for this source type" -- adding
/// a new storage format means registering a new factory here, not touching Harness
/// execution logic. Lookup is case-insensitive since source types round-trip through
/// task JSON and CLI arguments, both of which humans type by hand.
/// </summary>
public sealed class AdapterRegistry
{
    private readonly Dictionary<string, Func<IAmlDataAdapter>> _factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers (or replaces) the factory for a source type. Returns this instance for chaining.</summary>
    public AdapterRegistry Register(string sourceType, Func<IAmlDataAdapter> factory)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new ArgumentException("source type must not be empty", nameof(sourceType));
        _factories[sourceType] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>Every source type currently registered, for discovery/diagnostics (e.g. a CLI "list adapters" command or an UnsupportedSourceTypeException message).</summary>
    public IReadOnlyCollection<string> KnownSourceTypes => _factories.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Resolves and instantiates the adapter for a source type. Throws
    /// UnsupportedSourceTypeException (never returns null, never silently
    /// falls back to a default adapter) if the type isn't registered.
    /// </summary>
    public IAmlDataAdapter Resolve(string sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || !_factories.TryGetValue(sourceType, out var factory))
            throw new UnsupportedSourceTypeException(sourceType ?? string.Empty, KnownSourceTypes);
        return factory();
    }

    /// <summary>Instantiates every registered adapter once and reports its id/version -- for a manifest or CLI "adapters" listing. Never touches a real source (no I/O).</summary>
    public IReadOnlyList<AdapterDescriptor> DescribeRegisteredAdapters() =>
        _factories
            .Select(kv => new AdapterDescriptor(kv.Key, kv.Value().AdapterId, kv.Value().AdapterVersion))
            .OrderBy(d => d.SourceType, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The built-in registry: every adapter shipped with this codebase,
    /// pre-registered under its canonical source-type string. Callers that
    /// need a custom/extra adapter (e.g. a test double, or a future format)
    /// should call Register on the result rather than modifying this method.
    /// </summary>
    public static AdapterRegistry CreateDefault()
    {
        var registry = new AdapterRegistry();
        registry.Register("csv", () => new CsvDataAdapter());
        registry.Register("json", () => new JsonDataAdapter());
        registry.Register("jsonl", () => new JsonDataAdapter());
        registry.Register("parquet", () => new ParquetDataAdapter());
        registry.Register("postgresql", () => new PostgreSqlDataAdapter());
        registry.Register("sqlserver", () => new SqlServerDataAdapter());
        registry.Register("rest", () => new RestApiDataAdapter());
        registry.Register("neo4j", () => new Neo4jDataAdapter());
        registry.Register("graphml", () => new GraphMlDataAdapter());
        return registry;
    }
}

/// <summary>Adapter identity for manifests/diagnostics: the source-type key it's registered under, plus its own id/version.</summary>
public sealed record AdapterDescriptor(string SourceType, string AdapterId, string AdapterVersion);
