namespace AmlAgent.Adapters.Canonical;

/// <summary>
/// Validates that every evidence_ids / related-record / transaction-id
/// reference inside a merged CanonicalAmlCase actually resolves to a real
/// canonical record. Runs after CanonicalCaseMerger.Merge -- cross-source
/// reference validity only means something once every source's contribution
/// has been unified into one id-space. Never drops or silently repairs a
/// bad reference: every failure is reported, the caller decides what to do
/// with a case that fails validation (see LoadCaseCommand / assurance
/// wiring, which refuse to call a case assurance-valid while this fails).
/// </summary>
public static class EvidenceIntegrityValidator
{
    public static EvidenceIntegrityResult Validate(CanonicalAmlCase amlCase)
    {
        var index = BuildRecordTypeIndex(amlCase);

        var dangling = new List<EvidenceIntegrityIssue>();
        var incompatible = new List<EvidenceIntegrityIssue>();

        foreach (var rel in amlCase.Relationships)
            CheckEvidenceReferences(rel.EvidenceIds, "relationship", rel.RelationshipId, rel.SourceLineage, index, dangling, incompatible);

        foreach (var ev in amlCase.Evidence)
            CheckEvidenceReferences(ev.RelatedRecordIds, "evidence", ev.EvidenceId, ev.SourceLineage, index, dangling, incompatible);

        var missingTransactions = new List<EvidenceIntegrityIssue>();
        foreach (var sar in amlCase.Sars)
        {
            foreach (var txnId in sar.TransactionIds)
            {
                if (!index.TryGetValue(txnId, out var types) || !types.Contains("transaction"))
                {
                    missingTransactions.Add(new EvidenceIntegrityIssue(
                        "sar", sar.SarId, txnId, sar.SourceLineage,
                        $"sar '{sar.SarId}' references transaction '{txnId}', which is not present in the canonical case"));
                }
            }
        }

        var duplicates = amlCase.Conflicts
            .Where(c => c.RecordType == "evidence")
            .Select(c => new EvidenceIntegrityIssue("evidence", c.RecordId, c.RecordId, null,
                $"evidence '{c.RecordId}' was contributed with conflicting content by more than one source ({c.ConflictType}): {c.Description}"))
            .ToList();

        var status = dangling.Count == 0 && missingTransactions.Count == 0 && duplicates.Count == 0 && incompatible.Count == 0
            ? "passed" : "failed";

        return new EvidenceIntegrityResult(status, dangling, missingTransactions, duplicates, incompatible);
    }

    private static void CheckEvidenceReferences(
        IReadOnlyList<string> referencedIds,
        string referencingType,
        string referencingId,
        SourceLineage referencingLineage,
        Dictionary<string, HashSet<string>> index,
        List<EvidenceIntegrityIssue> dangling,
        List<EvidenceIntegrityIssue> incompatible)
    {
        foreach (var refId in referencedIds)
        {
            if (!index.TryGetValue(refId, out var types))
            {
                dangling.Add(new EvidenceIntegrityIssue(referencingType, referencingId, refId, referencingLineage,
                    $"{referencingType} '{referencingId}' references evidence id '{refId}', which does not exist anywhere in the canonical case"));
                continue;
            }

            // A reference is valid if it resolves to an actual evidence record or a
            // transaction (transactions are routinely cited as evidence directly by
            // their own id, e.g. a GraphML relationship's evidence_ids). Resolving to
            // any other record type (an account id that happens to collide with the
            // referenced string, etc.) means the id exists in the case but isn't
            // usable as evidence -- a real, distinct failure mode in multi-source
            // merges where id spaces from different sources can collide.
            if (types.Contains("evidence") || types.Contains("transaction"))
                continue;

            incompatible.Add(new EvidenceIntegrityIssue(referencingType, referencingId, refId, referencingLineage,
                $"{referencingType} '{referencingId}' references '{refId}' as evidence, but that id resolves to a {string.Join('/', types)} record, not evidence or a transaction"));
        }
    }

    private static Dictionary<string, HashSet<string>> BuildRecordTypeIndex(CanonicalAmlCase amlCase)
    {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, string type)
        {
            if (!index.TryGetValue(id, out var types))
                index[id] = types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            types.Add(type);
        }

        foreach (var t in amlCase.Transactions) Add(t.TransactionId, "transaction");
        foreach (var a in amlCase.Accounts) Add(a.AccountId, "account");
        foreach (var c in amlCase.Customers) Add(c.CustomerId, "customer");
        foreach (var e in amlCase.Entities) Add(e.EntityId, "entity");
        foreach (var c in amlCase.Cases) Add(c.CaseId, "case");
        foreach (var a in amlCase.Alerts) Add(a.AlertId, "alert");
        foreach (var e in amlCase.Evidence) Add(e.EvidenceId, "evidence");
        foreach (var j in amlCase.Jurisdictions) Add(j.Code, "jurisdiction");
        foreach (var s in amlCase.Sars) Add(s.SarId, "sar");

        return index;
    }
}

/// <summary>Result of EvidenceIntegrityValidator.Validate -- "passed" only if every list is empty.</summary>
public sealed record EvidenceIntegrityResult(
    string Status,
    IReadOnlyList<EvidenceIntegrityIssue> DanglingReferences,
    IReadOnlyList<EvidenceIntegrityIssue> MissingTransactionReferences,
    IReadOnlyList<EvidenceIntegrityIssue> DuplicateEvidenceIds,
    IReadOnlyList<EvidenceIntegrityIssue> IncompatibleEvidenceTypes)
{
    public bool Passed => Status == "passed";
}

/// <summary>One evidence-integrity failure: who referenced what, and why it's invalid. SourceLineage of the referencing record is preserved so the failure can be traced back to the source that introduced it.</summary>
public sealed record EvidenceIntegrityIssue(
    string ReferencingRecordType,
    string ReferencingRecordId,
    string EvidenceId,
    SourceLineage? ReferencingSourceLineage,
    string Description);
