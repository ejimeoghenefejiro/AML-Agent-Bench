namespace AmlAgent.Adapters.Canonical;

/// <summary>
/// What a single IAmlDataAdapter.LoadAsync call produces: one source's
/// contribution to a case, normalised into the canonical model. Most
/// adapters will only populate a subset of these collections (e.g. a flat
/// CSV transaction ledger only populates Transactions) -- empty collections
/// are the honest default, never fabricated placeholder records.
/// </summary>
public sealed record CanonicalAmlDataset(
    string SchemaVersion,
    IReadOnlyList<CanonicalTransaction> Transactions,
    IReadOnlyList<CanonicalAccount> Accounts,
    IReadOnlyList<CanonicalCustomer> Customers,
    IReadOnlyList<CanonicalEntity> Entities,
    IReadOnlyList<CanonicalRelationship> Relationships,
    IReadOnlyList<CanonicalCase> Cases,
    IReadOnlyList<CanonicalAlert> Alerts,
    IReadOnlyList<CanonicalEvidence> Evidence,
    IReadOnlyList<CanonicalJurisdiction> Jurisdictions,
    IReadOnlyList<CanonicalSar> Sars)
{
    public static CanonicalAmlDataset Empty(string schemaVersion = CanonicalSchema.Version) => new(
        schemaVersion,
        Array.Empty<CanonicalTransaction>(),
        Array.Empty<CanonicalAccount>(),
        Array.Empty<CanonicalCustomer>(),
        Array.Empty<CanonicalEntity>(),
        Array.Empty<CanonicalRelationship>(),
        Array.Empty<CanonicalCase>(),
        Array.Empty<CanonicalAlert>(),
        Array.Empty<CanonicalEvidence>(),
        Array.Empty<CanonicalJurisdiction>(),
        Array.Empty<CanonicalSar>());

    /// <summary>Total record count across every collection -- used for manifest.record_count.</summary>
    public int TotalRecordCount =>
        Transactions.Count + Accounts.Count + Customers.Count + Entities.Count +
        Relationships.Count + Cases.Count + Alerts.Count + Evidence.Count +
        Jurisdictions.Count + Sars.Count;
}

/// <summary>
/// A case built by merging one or more CanonicalAmlDataset contributions
/// (CLI-Only spec section 9/10: "several adapters contribute to one
/// canonical case package"). Conflicts detected during the merge are
/// recorded, never silently resolved by overwriting.
/// </summary>
public sealed record CanonicalAmlCase(
    string SchemaVersion,
    IReadOnlyList<CanonicalTransaction> Transactions,
    IReadOnlyList<CanonicalAccount> Accounts,
    IReadOnlyList<CanonicalCustomer> Customers,
    IReadOnlyList<CanonicalEntity> Entities,
    IReadOnlyList<CanonicalRelationship> Relationships,
    IReadOnlyList<CanonicalCase> Cases,
    IReadOnlyList<CanonicalAlert> Alerts,
    IReadOnlyList<CanonicalEvidence> Evidence,
    IReadOnlyList<CanonicalJurisdiction> Jurisdictions,
    IReadOnlyList<CanonicalSar> Sars,
    IReadOnlyList<MergeConflict> Conflicts,
    IReadOnlyList<SourceManifestEntry> SourceManifest);

/// <summary>One detected problem while merging multiple sources into a case -- never auto-resolved.</summary>
public sealed record MergeConflict(
    string RecordType,
    string RecordId,
    string ConflictType, // "duplicate_id" | "conflicting_value" | "timestamp_mismatch" | "currency_mismatch" | "missing_reference" | "invalid_relationship" | "incompatible_schema"
    string Description);

/// <summary>One source's contribution summary within a merged case.</summary>
public sealed record SourceManifestEntry(
    string SourceType,
    string? SourceName,
    string Adapter,
    string AdapterVersion,
    int RecordCount,
    string DatasetHash);
