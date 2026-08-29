using AmlAgent.Evidence;

namespace AmlAgent.Adapters.Canonical;

/// <summary>
/// Converts a canonical AML dataset/case into the flat evidence universe
/// AmlAgent.Evidence.EvidenceScoring's generalised traceability scorer needs
/// (AmlAgent.Evidence.EvidenceReference) -- every record across all ten
/// canonical types is citable evidence, not just transactions. This is the
/// bridge between the multi-source data-adapter layer (which already knows
/// about relationships, SARs, watchlist entries, etc via CanonicalAmlCase)
/// and the traceability scorer (which previously only understood flat
/// transaction-id sets).
/// </summary>
public static class CanonicalAmlCaseEvidenceExtensions
{
    public static IReadOnlyList<EvidenceReference> ToEvidenceReferences(this CanonicalAmlDataset dataset)
    {
        var refs = new List<EvidenceReference>();
        foreach (var t in dataset.Transactions) refs.Add(Reference(t.TransactionId, "transaction", t.SourceLineage));
        foreach (var a in dataset.Accounts) refs.Add(Reference(a.AccountId, "account", a.SourceLineage));
        foreach (var c in dataset.Customers) refs.Add(Reference(c.CustomerId, "customer", c.SourceLineage));
        foreach (var e in dataset.Entities) refs.Add(Reference(e.EntityId, "entity", e.SourceLineage));
        foreach (var r in dataset.Relationships) refs.Add(Reference(r.RelationshipId, "relationship", r.SourceLineage));
        foreach (var c in dataset.Cases) refs.Add(Reference(c.CaseId, "case", c.SourceLineage));
        foreach (var a in dataset.Alerts) refs.Add(Reference(a.AlertId, "alert", a.SourceLineage));
        foreach (var e in dataset.Evidence) refs.Add(Reference(e.EvidenceId, "evidence", e.SourceLineage));
        foreach (var j in dataset.Jurisdictions) refs.Add(Reference(j.Code, "jurisdiction", j.SourceLineage));
        foreach (var s in dataset.Sars) refs.Add(Reference(s.SarId, "sar", s.SourceLineage));
        return refs;
    }

    public static IReadOnlyList<EvidenceReference> ToEvidenceReferences(this CanonicalAmlCase amlCase)
    {
        var refs = new List<EvidenceReference>();
        foreach (var t in amlCase.Transactions) refs.Add(Reference(t.TransactionId, "transaction", t.SourceLineage));
        foreach (var a in amlCase.Accounts) refs.Add(Reference(a.AccountId, "account", a.SourceLineage));
        foreach (var c in amlCase.Customers) refs.Add(Reference(c.CustomerId, "customer", c.SourceLineage));
        foreach (var e in amlCase.Entities) refs.Add(Reference(e.EntityId, "entity", e.SourceLineage));
        foreach (var r in amlCase.Relationships) refs.Add(Reference(r.RelationshipId, "relationship", r.SourceLineage));
        foreach (var c in amlCase.Cases) refs.Add(Reference(c.CaseId, "case", c.SourceLineage));
        foreach (var a in amlCase.Alerts) refs.Add(Reference(a.AlertId, "alert", a.SourceLineage));
        foreach (var e in amlCase.Evidence) refs.Add(Reference(e.EvidenceId, "evidence", e.SourceLineage));
        foreach (var j in amlCase.Jurisdictions) refs.Add(Reference(j.Code, "jurisdiction", j.SourceLineage));
        foreach (var s in amlCase.Sars) refs.Add(Reference(s.SarId, "sar", s.SourceLineage));
        return refs;
    }

    private static EvidenceReference Reference(string id, string evidenceType, SourceLineage lineage) =>
        new(EvidenceId: id, EvidenceType: evidenceType, Source: lineage.SourceType);
}
