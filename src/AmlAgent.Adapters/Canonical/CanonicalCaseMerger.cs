using AmlAgent.Adapters.Normalisation;

namespace AmlAgent.Adapters.Canonical;

/// <summary>
/// Merges several adapters' CanonicalAmlDataset contributions into one
/// CanonicalAmlCase (CLI-Only spec: "several adapters contribute to one
/// canonical case package"). Never silently overwrites conflicting data --
/// every collision is recorded as a MergeConflict, and the merge still
/// completes (a bank investigator needs the merged case even when two
/// sources disagree; the conflict list is what they check next).
///
/// Merge rules (documented since MergeConflict.ConflictType has no single
/// canonical spec definition beyond its allowed values):
///   - Same record id in &gt;1 source, identical content  -&gt; silently deduped, one copy kept.
///   - Same id, differing Timestamp (transactions only)   -&gt; "timestamp_mismatch".
///   - Same id, differing Currency (transactions only)    -&gt; "currency_mismatch".
///   - Same id, any other field differs                   -&gt; "conflicting_value".
///   - A relationship's source/target entity id is absent
///     from the merged entity set                         -&gt; "missing_reference".
///   - A source dataset's SchemaVersion doesn't match the
///     first source's (whole dataset excluded from merge)  -&gt; "incompatible_schema".
/// First-seen source (by input order) wins for the kept value on every conflict.
/// </summary>
public static class CanonicalCaseMerger
{
    public static CanonicalAmlCase Merge(IReadOnlyList<CanonicalAmlDataset> sources)
    {
        if (sources.Count == 0)
            return new CanonicalAmlCase(CanonicalSchema.Version,
                Array.Empty<CanonicalTransaction>(), Array.Empty<CanonicalAccount>(), Array.Empty<CanonicalCustomer>(),
                Array.Empty<CanonicalEntity>(), Array.Empty<CanonicalRelationship>(), Array.Empty<CanonicalCase>(),
                Array.Empty<CanonicalAlert>(), Array.Empty<CanonicalEvidence>(), Array.Empty<CanonicalJurisdiction>(),
                Array.Empty<CanonicalSar>(), Array.Empty<MergeConflict>(), Array.Empty<SourceManifestEntry>());

        var conflicts = new List<MergeConflict>();
        var schemaVersion = sources[0].SchemaVersion;
        var usable = new List<CanonicalAmlDataset>();
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i].SchemaVersion != schemaVersion)
            {
                conflicts.Add(new MergeConflict("dataset", $"source#{i}", "incompatible_schema",
                    $"source #{i} has schema_version '{sources[i].SchemaVersion}', expected '{schemaVersion}' (matching source #0) -- excluded from the merge"));
                continue;
            }
            usable.Add(sources[i]);
        }

        var (transactions, txnConflicts) = MergeById(usable.Select(d => d.Transactions), t => t.TransactionId,
            "transaction", TransactionsContentEqual, TransactionConflictType, TransactionDescription);
        conflicts.AddRange(txnConflicts);

        var (accounts, acctConflicts) = MergeById(usable.Select(d => d.Accounts), a => a.AccountId, "account",
            (a, b) => a.Owner == b.Owner && a.Institution == b.Institution && a.Currency == b.Currency,
            (a, b) => "conflicting_value", (a, b) => $"account '{a.AccountId}' differs across sources");
        conflicts.AddRange(acctConflicts);

        var (customers, custConflicts) = MergeById(usable.Select(d => d.Customers), c => c.CustomerId, "customer",
            (a, b) => a.Name == b.Name && a.RiskRating == b.RiskRating && a.Jurisdiction == b.Jurisdiction,
            (a, b) => "conflicting_value", (a, b) => $"customer '{a.CustomerId}' differs across sources");
        conflicts.AddRange(custConflicts);

        var (entities, entConflicts) = MergeById(usable.Select(d => d.Entities), e => e.EntityId, "entity",
            (a, b) => a.EntityType == b.EntityType && a.DisplayName == b.DisplayName,
            (a, b) => "conflicting_value", (a, b) => $"entity '{a.EntityId}' differs across sources");
        conflicts.AddRange(entConflicts);

        var (relationships, relConflicts) = MergeById(usable.Select(d => d.Relationships), r => r.RelationshipId, "relationship",
            (a, b) => a.SourceEntityId == b.SourceEntityId && a.TargetEntityId == b.TargetEntityId &&
                      a.RelationshipType == b.RelationshipType && a.EvidenceIds.SequenceEqual(b.EvidenceIds),
            (a, b) => "conflicting_value", (a, b) => $"relationship '{a.RelationshipId}' differs across sources");
        conflicts.AddRange(relConflicts);

        var entityIds = new HashSet<string>(entities.Select(e => e.EntityId), StringComparer.OrdinalIgnoreCase);
        foreach (var rel in relationships)
        {
            if (!entityIds.Contains(rel.SourceEntityId))
                conflicts.Add(new MergeConflict("relationship", rel.RelationshipId, "missing_reference",
                    $"relationship '{rel.RelationshipId}' references unknown source entity '{rel.SourceEntityId}'"));
            if (!entityIds.Contains(rel.TargetEntityId))
                conflicts.Add(new MergeConflict("relationship", rel.RelationshipId, "missing_reference",
                    $"relationship '{rel.RelationshipId}' references unknown target entity '{rel.TargetEntityId}'"));
        }

        var (cases, caseConflicts) = MergeById(usable.Select(d => d.Cases), c => c.CaseId, "case",
            (a, b) => a.Title == b.Title && a.Status == b.Status,
            (a, b) => "conflicting_value", (a, b) => $"case '{a.CaseId}' differs across sources");
        conflicts.AddRange(caseConflicts);

        var (alerts, alertConflicts) = MergeById(usable.Select(d => d.Alerts), a => a.AlertId, "alert",
            (a, b) => a.CaseId == b.CaseId && a.Typology == b.Typology && a.Severity == b.Severity,
            (a, b) => "conflicting_value", (a, b) => $"alert '{a.AlertId}' differs across sources");
        conflicts.AddRange(alertConflicts);

        var (evidence, evidConflicts) = MergeById(usable.Select(d => d.Evidence), e => e.EvidenceId, "evidence",
            (a, b) => a.EvidenceType == b.EvidenceType && a.Description == b.Description && a.RelatedRecordIds.SequenceEqual(b.RelatedRecordIds),
            (a, b) => "conflicting_value", (a, b) => $"evidence '{a.EvidenceId}' differs across sources");
        conflicts.AddRange(evidConflicts);

        var (jurisdictions, jurisConflicts) = MergeById(usable.Select(d => d.Jurisdictions), j => j.Code, "jurisdiction",
            (a, b) => a.Name == b.Name && a.HighRisk == b.HighRisk,
            (a, b) => "conflicting_value", (a, b) => $"jurisdiction '{a.Code}' differs across sources");
        conflicts.AddRange(jurisConflicts);

        var (sars, sarConflicts) = MergeById(usable.Select(d => d.Sars), s => s.SarId, "sar",
            (a, b) => a.CaseId == b.CaseId && a.TransactionIds.SequenceEqual(b.TransactionIds) && a.Narrative == b.Narrative,
            (a, b) => "conflicting_value", (a, b) => $"sar '{a.SarId}' differs across sources");
        conflicts.AddRange(sarConflicts);

        var sourceManifest = sources.Select((d, i) => DescribeSource(d, i)).ToList();

        return new CanonicalAmlCase(schemaVersion, transactions, accounts, customers, entities, relationships,
            cases, alerts, evidence, jurisdictions, sars, conflicts, sourceManifest);
    }

    private static bool TransactionsContentEqual(CanonicalTransaction a, CanonicalTransaction b) =>
        a.SourceAccount == b.SourceAccount && a.DestinationAccount == b.DestinationAccount &&
        a.Amount == b.Amount && a.Currency == b.Currency && a.Timestamp == b.Timestamp &&
        a.Channel == b.Channel && a.Jurisdiction == b.Jurisdiction && a.SarLinked == b.SarLinked;

    private static string TransactionConflictType(CanonicalTransaction a, CanonicalTransaction b) =>
        a.Timestamp != b.Timestamp ? "timestamp_mismatch" :
        a.Currency != b.Currency ? "currency_mismatch" :
        "conflicting_value";

    private static string TransactionDescription(CanonicalTransaction a, CanonicalTransaction b) =>
        a.Timestamp != b.Timestamp
            ? $"transaction '{a.TransactionId}' has conflicting timestamps ({a.Timestamp:O} vs {b.Timestamp:O}) across sources"
            : a.Currency != b.Currency
                ? $"transaction '{a.TransactionId}' has conflicting currencies ({a.Currency} vs {b.Currency}) across sources"
                : $"transaction '{a.TransactionId}' differs across sources";

    /// <summary>
    /// Unions records from multiple sources by id. Identical content for the same id is
    /// deduped silently; differing content keeps the first-seen record and records a conflict.
    /// </summary>
    private static (List<T> Merged, List<MergeConflict> Conflicts) MergeById<T>(
        IEnumerable<IReadOnlyList<T>> perSourceRecords,
        Func<T, string> idSelector,
        string recordType,
        Func<T, T, bool> contentEquals,
        Func<T, T, string> conflictType,
        Func<T, T, string> describeConflict)
    {
        var byId = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<MergeConflict>();
        var merged = new List<T>();

        foreach (var records in perSourceRecords)
        {
            foreach (var record in records)
            {
                var id = idSelector(record);
                if (byId.TryGetValue(id, out var existing))
                {
                    // Same id from >1 source: identical content is a healthy overlap
                    // (silently deduped, first-seen copy kept); differing content is a
                    // real conflict -- recorded, never silently overwritten.
                    if (!contentEquals(existing, record))
                        conflicts.Add(new MergeConflict(recordType, id, conflictType(existing, record), describeConflict(existing, record)));
                }
                else
                {
                    byId[id] = record;
                    merged.Add(record);
                }
            }
        }

        return (merged, conflicts);
    }

    private static SourceManifestEntry DescribeSource(CanonicalAmlDataset dataset, int index)
    {
        var lineage =
            dataset.Transactions.FirstOrDefault()?.SourceLineage ??
            dataset.Accounts.FirstOrDefault()?.SourceLineage ??
            dataset.Customers.FirstOrDefault()?.SourceLineage ??
            dataset.Entities.FirstOrDefault()?.SourceLineage ??
            dataset.Relationships.FirstOrDefault()?.SourceLineage ??
            dataset.Cases.FirstOrDefault()?.SourceLineage ??
            dataset.Alerts.FirstOrDefault()?.SourceLineage ??
            dataset.Evidence.FirstOrDefault()?.SourceLineage ??
            dataset.Jurisdictions.FirstOrDefault()?.SourceLineage ??
            dataset.Sars.FirstOrDefault()?.SourceLineage;

        return new SourceManifestEntry(
            SourceType: lineage?.SourceType ?? $"unknown-source-{index}",
            SourceName: lineage?.SourceName,
            Adapter: lineage?.Adapter ?? "unknown",
            AdapterVersion: lineage?.AdapterVersion ?? "unknown",
            RecordCount: dataset.TotalRecordCount,
            DatasetHash: CanonicalHashing.ComputeNormalisationHash(dataset));
    }
}
