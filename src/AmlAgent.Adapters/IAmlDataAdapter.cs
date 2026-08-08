using AmlAgent.Adapters.Canonical;

namespace AmlAgent.Adapters;

/// <summary>
/// The common contract every storage-format adapter implements. A task or
/// the Harness only ever talks to this interface -- never to a CSV parser,
/// a SQL connection, or a Neo4j driver directly. That separation is the
/// entire point of this layer: Storage Format -&gt; Data Adapter -&gt;
/// Canonical AML Schema -&gt; Scenario/Task -&gt; Harness -&gt; Assurance.
/// </summary>
public interface IAmlDataAdapter
{
    /// <summary>Stable identifier used in the adapter registry and recorded in every manifest (e.g. "csv", "sqlserver").</summary>
    string AdapterId { get; }

    /// <summary>This adapter's own version, independent of the canonical schema version -- bump when the adapter's normalisation logic changes.</summary>
    string AdapterVersion { get; }

    Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base for every adapter-raised error, so callers can catch one type and
/// still get a clear, specific message (CLI-Only spec section 19: "Adapter
/// failures must produce clear CLI errors").
/// </summary>
public abstract class AdapterException : Exception
{
    protected AdapterException(string message) : base(message) { }
    protected AdapterException(string message, Exception inner) : base(message, inner) { }
}

public sealed class UnsupportedSourceTypeException : AdapterException
{
    public UnsupportedSourceTypeException(string sourceType, IEnumerable<string> knownTypes)
        : base($"Unsupported source type '{sourceType}'. Known types: {string.Join(", ", knownTypes)}") { }
}

public sealed class InvalidAdapterConfigurationException : AdapterException
{
    public InvalidAdapterConfigurationException(string adapterId, string reason)
        : base($"Invalid configuration for adapter '{adapterId}': {reason}") { }
}

public sealed class AdapterSourceException : AdapterException
{
    public AdapterSourceException(string adapterId, string reason)
        : base($"Adapter '{adapterId}' could not read its source: {reason}") { }

    public AdapterSourceException(string adapterId, string reason, Exception inner)
        : base($"Adapter '{adapterId}' could not read its source: {reason}", inner) { }
}

public sealed class AdapterNormalisationException : AdapterException
{
    public AdapterNormalisationException(string adapterId, string reason)
        : base($"Adapter '{adapterId}' could not normalise a record: {reason}") { }
}
