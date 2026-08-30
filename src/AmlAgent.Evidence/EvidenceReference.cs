namespace AmlAgent.Evidence;

/// <summary>
/// One evidence item an agent's report could cite -- generalises "transaction
/// ID" to any evidence type the canonical AML case model can produce:
/// transaction, account, customer, entity, relationship, case, alert,
/// evidence record, jurisdiction, or SAR (matching the record types
/// AmlAgent.Adapters.Canonical.CanonicalAmlCase already carries), or an
/// arbitrary source-specific record type with no canonical type yet.
///
/// This is what makes cross-source evidence -- a relationship-graph edge, a
/// watchlist entry, a SAR filing, a KYC record -- recognisable to the
/// traceability scorer at all. Previously only transaction-ID-shaped tokens
/// (matching a fixed regex) were ever extracted from report text; a report
/// correctly citing a relationship id like "R1" got zero credit (fixed by
/// EvidenceScoring.ExtractCitedEvidenceIds) and a report fabricating one went
/// completely undetected (fixed by EvidenceScoring.InferEvidenceIdShapes /
/// ExtractShapeFabricatedIds, v0.3 validation-priorities fix #3). See
/// docs/evidence-traceability-framework.md#traceability-failure-taxonomy for
/// the gap this closes, and EvidenceScoring.ExtractCitedEvidenceIds /
/// EvidenceScoring.ComputeTraceability(string, IReadOnlyCollection&lt;EvidenceReference&gt;, ...)
/// for where it's used.
///
/// EntityId and RecordKey are optional secondary keys some evidence types
/// carry alongside their primary EvidenceId -- e.g. a relationship's
/// associated entity, or a source system's own record identifier distinct
/// from the canonical id assigned during normalisation. Both are informational
/// only today; citation matching is keyed on EvidenceId alone.
/// </summary>
public sealed record EvidenceReference(
    string EvidenceId,
    string EvidenceType,
    string? Source = null,
    string? EntityId = null,
    string? RecordKey = null);
