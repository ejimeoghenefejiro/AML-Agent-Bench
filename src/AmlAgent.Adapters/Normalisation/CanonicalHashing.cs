using System.Security.Cryptography;
using System.Text;
using AmlAgent.Adapters.Canonical;

namespace AmlAgent.Adapters.Normalisation;

/// <summary>
/// dataset_hash represents the source snapshot as adapters received it;
/// normalisation_hash represents the canonical dataset after adapter
/// processing (CLI-Only spec section 13). Together they can reveal source
/// data changes, adapter behaviour changes, and normalisation regressions
/// separately -- if only one changes, you know which side of the adapter
/// boundary to look at.
///
/// Deterministic ordering is applied before hashing every collection (by
/// each record's own ID, ordinal comparison) so the same logical dataset
/// always hashes the same way regardless of source row order -- required
/// for "same source snapshot -&gt; same normalised hash".
/// </summary>
public static class CanonicalHashing
{
    public static string ComputeDatasetHash(byte[] rawSourceBytes)
    {
        var bytes = SHA256.HashData(rawSourceBytes);
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ComputeDatasetHash(IEnumerable<byte[]> rawSourceChunks)
    {
        using var sha = SHA256.Create();
        foreach (var chunk in rawSourceChunks)
            sha.TransformBlock(chunk, 0, chunk.Length, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return "sha256:" + Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    public static string ComputeNormalisationHash(CanonicalAmlDataset dataset) => ComputeContentHash(
        dataset.SchemaVersion, dataset.Transactions, dataset.Accounts, dataset.Customers, dataset.Entities,
        dataset.Relationships, dataset.Cases, dataset.Alerts, dataset.Evidence, dataset.Jurisdictions, dataset.Sars);

    /// <summary>
    /// Hashes a merged CanonicalAmlCase the same way ComputeNormalisationHash hashes a
    /// single dataset -- over the canonical record content only (Conflicts and
    /// SourceManifest are merge-process metadata, not case content: if a source
    /// changes in a way that matters, the kept record content already changes,
    /// which this hash already reflects). Same case inputs -&gt; same hash; a
    /// changed source -&gt; a changed hash.
    /// </summary>
    public static string ComputeCaseHash(CanonicalAmlCase amlCase) => ComputeContentHash(
        amlCase.SchemaVersion, amlCase.Transactions, amlCase.Accounts, amlCase.Customers, amlCase.Entities,
        amlCase.Relationships, amlCase.Cases, amlCase.Alerts, amlCase.Evidence, amlCase.Jurisdictions, amlCase.Sars);

    private static string ComputeContentHash(
        string schemaVersion,
        IReadOnlyList<CanonicalTransaction> transactions,
        IReadOnlyList<CanonicalAccount> accounts,
        IReadOnlyList<CanonicalCustomer> customers,
        IReadOnlyList<CanonicalEntity> entities,
        IReadOnlyList<CanonicalRelationship> relationships,
        IReadOnlyList<CanonicalCase> cases,
        IReadOnlyList<CanonicalAlert> alerts,
        IReadOnlyList<CanonicalEvidence> evidence,
        IReadOnlyList<CanonicalJurisdiction> jurisdictions,
        IReadOnlyList<CanonicalSar> sars)
    {
        var sb = new StringBuilder();
        sb.Append("schema=").Append(schemaVersion).Append(';');

        AppendOrdered(sb, "txn", transactions, t => t.TransactionId, t =>
            $"{t.TransactionId}|{t.SourceAccount}|{t.DestinationAccount}|{t.Amount}|{t.Currency}|{t.Timestamp:O}|{t.Channel}|{t.Jurisdiction}|{t.SarLinked}");
        AppendOrdered(sb, "acct", accounts, a => a.AccountId, a =>
            $"{a.AccountId}|{a.Owner}|{a.Institution}|{a.Currency}");
        AppendOrdered(sb, "cust", customers, c => c.CustomerId, c =>
            $"{c.CustomerId}|{c.Name}|{c.RiskRating}|{c.Jurisdiction}");
        AppendOrdered(sb, "ent", entities, e => e.EntityId, e =>
            $"{e.EntityId}|{e.EntityType}|{e.DisplayName}");
        AppendOrdered(sb, "rel", relationships, r => r.RelationshipId, r =>
            $"{r.RelationshipId}|{r.SourceEntityId}|{r.TargetEntityId}|{r.RelationshipType}|{string.Join(',', r.EvidenceIds.OrderBy(x => x, StringComparer.Ordinal))}");
        AppendOrdered(sb, "case", cases, c => c.CaseId, c =>
            $"{c.CaseId}|{c.Title}|{c.Status}");
        AppendOrdered(sb, "alert", alerts, a => a.AlertId, a =>
            $"{a.AlertId}|{a.CaseId}|{a.Typology}|{a.Severity}");
        AppendOrdered(sb, "evid", evidence, e => e.EvidenceId, e =>
            $"{e.EvidenceId}|{e.EvidenceType}|{e.Description}|{string.Join(',', e.RelatedRecordIds.OrderBy(x => x, StringComparer.Ordinal))}");
        AppendOrdered(sb, "juris", jurisdictions, j => j.Code, j =>
            $"{j.Code}|{j.Name}|{j.HighRisk}");
        AppendOrdered(sb, "sar", sars, s => s.SarId, s =>
            $"{s.SarId}|{s.CaseId}|{string.Join(',', s.TransactionIds.OrderBy(x => x, StringComparer.Ordinal))}|{s.Narrative}");

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void AppendOrdered<T>(StringBuilder sb, string label, IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, string> serialise)
    {
        sb.Append(label).Append('=');
        foreach (var item in items.OrderBy(keySelector, StringComparer.Ordinal))
            sb.Append(serialise(item)).Append(';');
        sb.Append('|');
    }
}
