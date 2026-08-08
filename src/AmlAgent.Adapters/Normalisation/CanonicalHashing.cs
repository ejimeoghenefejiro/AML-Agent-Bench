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

    public static string ComputeNormalisationHash(CanonicalAmlDataset dataset)
    {
        var sb = new StringBuilder();
        sb.Append("schema=").Append(dataset.SchemaVersion).Append(';');

        AppendOrdered(sb, "txn", dataset.Transactions, t => t.TransactionId, t =>
            $"{t.TransactionId}|{t.SourceAccount}|{t.DestinationAccount}|{t.Amount}|{t.Currency}|{t.Timestamp:O}|{t.Channel}|{t.Jurisdiction}|{t.SarLinked}");
        AppendOrdered(sb, "acct", dataset.Accounts, a => a.AccountId, a =>
            $"{a.AccountId}|{a.Owner}|{a.Institution}|{a.Currency}");
        AppendOrdered(sb, "cust", dataset.Customers, c => c.CustomerId, c =>
            $"{c.CustomerId}|{c.Name}|{c.RiskRating}|{c.Jurisdiction}");
        AppendOrdered(sb, "ent", dataset.Entities, e => e.EntityId, e =>
            $"{e.EntityId}|{e.EntityType}|{e.DisplayName}");
        AppendOrdered(sb, "rel", dataset.Relationships, r => r.RelationshipId, r =>
            $"{r.RelationshipId}|{r.SourceEntityId}|{r.TargetEntityId}|{r.RelationshipType}|{string.Join(',', r.EvidenceIds.OrderBy(x => x, StringComparer.Ordinal))}");
        AppendOrdered(sb, "case", dataset.Cases, c => c.CaseId, c =>
            $"{c.CaseId}|{c.Title}|{c.Status}");
        AppendOrdered(sb, "alert", dataset.Alerts, a => a.AlertId, a =>
            $"{a.AlertId}|{a.CaseId}|{a.Typology}|{a.Severity}");
        AppendOrdered(sb, "evid", dataset.Evidence, e => e.EvidenceId, e =>
            $"{e.EvidenceId}|{e.EvidenceType}|{e.Description}|{string.Join(',', e.RelatedRecordIds.OrderBy(x => x, StringComparer.Ordinal))}");
        AppendOrdered(sb, "juris", dataset.Jurisdictions, j => j.Code, j =>
            $"{j.Code}|{j.Name}|{j.HighRisk}");
        AppendOrdered(sb, "sar", dataset.Sars, s => s.SarId, s =>
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
