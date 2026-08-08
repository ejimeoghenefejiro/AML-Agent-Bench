namespace AmlAgent.Adapters.Canonical;

/// <summary>
/// The canonical AML data model's own version, independent of the benchmark
/// version or any individual adapter's version. A breaking change to the
/// canonical record shapes requires bumping this. Consumers (manifests,
/// merge logic) should reject or explicitly migrate a dataset whose
/// SchemaVersion doesn't match, rather than silently assuming compatibility.
/// </summary>
public static class CanonicalSchema
{
    public const string Version = "aml-canonical-1.0";
}
