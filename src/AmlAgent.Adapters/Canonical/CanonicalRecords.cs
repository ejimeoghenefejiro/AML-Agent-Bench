namespace AmlAgent.Adapters.Canonical;

/// <summary>
/// The canonical AML data model (schema version <see cref="CanonicalSchema.Version"/>).
/// Every adapter normalises source-specific data into these records before
/// any task or the Harness ever sees it -- a task must never need to know
/// whether a CanonicalTransaction originally came from a CSV row, a SQL
/// Server table, or a Neo4j relationship.
/// </summary>
public sealed record CanonicalTransaction(
    string TransactionId,
    string SourceAccount,
    string DestinationAccount,
    decimal Amount,
    string? Currency,
    DateTimeOffset Timestamp,
    string? Channel,
    string? Jurisdiction,
    bool SarLinked,
    SourceLineage SourceLineage);

public sealed record CanonicalAccount(
    string AccountId,
    string? Owner,
    string? Institution,
    string? Currency,
    SourceLineage SourceLineage);

public sealed record CanonicalCustomer(
    string CustomerId,
    string? Name,
    string? RiskRating,
    string? Jurisdiction,
    SourceLineage SourceLineage);

/// <summary>A generic node in the entity graph -- an account, customer, company, device, etc.</summary>
public sealed record CanonicalEntity(
    string EntityId,
    string EntityType,
    string? DisplayName,
    SourceLineage SourceLineage);

/// <summary>An edge in the entity graph (e.g. "transferred_to", "owns", "shares_device_with").</summary>
public sealed record CanonicalRelationship(
    string RelationshipId,
    string SourceEntityId,
    string TargetEntityId,
    string RelationshipType,
    IReadOnlyList<string> EvidenceIds,
    SourceLineage SourceLineage);

public sealed record CanonicalCase(
    string CaseId,
    string? Title,
    string? Status,
    SourceLineage SourceLineage);

public sealed record CanonicalAlert(
    string AlertId,
    string? CaseId,
    string? Typology,
    string? Severity,
    SourceLineage SourceLineage);

public sealed record CanonicalEvidence(
    string EvidenceId,
    string EvidenceType,
    string? Description,
    IReadOnlyList<string> RelatedRecordIds,
    SourceLineage SourceLineage);

public sealed record CanonicalJurisdiction(
    string Code,
    string? Name,
    bool HighRisk,
    SourceLineage SourceLineage);

public sealed record CanonicalSar(
    string SarId,
    string? CaseId,
    IReadOnlyList<string> TransactionIds,
    string? Narrative,
    SourceLineage SourceLineage);
