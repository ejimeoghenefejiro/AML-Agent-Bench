using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AmlAgent.Evidence;

/// <summary>
/// Pure, dependency-free scoring logic for the two primary PhD-proposal
/// metrics: Evidence-Grounded Hallucination Rate (EGHR) and evidence
/// traceability (citation precision/recall). No LLM, network or file I/O
/// happens in this class, so it is directly unit-testable without a
/// workspace or an OPENAI_API_KEY.
///
/// Citation-existence checking (is a cited transaction ID real?) is always
/// deterministic here — callers (e.g. the LLM-as-judge) cannot mark a
/// fabricated citation as "supported".
/// </summary>
public static class EvidenceScoring
{
    private static readonly Regex TxnIdPattern = new(@"\bT[123]-\d{3}\b", RegexOptions.Compiled);

    /// <summary>Parses the set of real transaction IDs out of a CSV's text content.</summary>
    public static HashSet<string> ParseTxnIdsFromCsv(string csvContent, string idColumn = "txn_id")
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(csvContent)) return ids;

        var lines = csvContent.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0) return ids;

        var header = lines[0].Split(',');
        var idCol = Array.IndexOf(header, idColumn);
        if (idCol < 0) return ids;

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',');
            if (idCol >= cols.Length) continue;
            var id = cols[idCol].Trim();
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Parses the set of real transaction IDs out of JSON grounding data.
    /// Accepts a top-level JSON array of objects (each with an idField
    /// property), or an object wrapping that array under a common key
    /// ("transactions", "rows", "data", "transfers", "records" -- whichever
    /// is present first). Malformed JSON or an unrecognised shape returns
    /// an empty set rather than throwing, matching ParseTxnIdsFromCsv's
    /// "no data available" behaviour on a missing column.
    /// </summary>
    public static HashSet<string> ParseTxnIdsFromJson(string jsonContent, string idField = "txn_id")
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(jsonContent)) return ids;

        JsonNode? root;
        try { root = JsonNode.Parse(jsonContent); }
        catch (JsonException) { return ids; }

        var array = root as JsonArray;
        if (array is null && root is JsonObject obj)
        {
            foreach (var key in new[] { "transactions", "rows", "data", "transfers", "records" })
            {
                if (obj[key] is JsonArray candidate) { array = candidate; break; }
            }
        }
        if (array is null) return ids;

        foreach (var item in array)
        {
            var id = item?[idField]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Parses transaction IDs out of a grounding file's content, choosing
    /// CSV or JSON parsing by the file's extension. An unrecognised
    /// extension returns an empty set (no data contributed), not an
    /// exception -- callers can still record that the file existed but
    /// wasn't a supported format.
    /// </summary>
    public static HashSet<string> ParseTxnIdsFromFile(string content, string fileName, string idField = "txn_id")
    {
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return ParseTxnIdsFromJson(content, idField);
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return ParseTxnIdsFromCsv(content, idField);
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every transaction-ID-shaped token in free text, including duplicates.</summary>
    public static List<string> ExtractCitedTxnIds(string text) =>
        TxnIdPattern.Matches(text ?? "").Select(m => m.Value).ToList();

    /// <summary>
    /// Evidence-traceability metric: citation precision/recall of a report's
    /// cited transaction IDs against a curated gold-evidence set. Entirely
    /// regex + set arithmetic — no LLM involved, fully reproducible.
    /// </summary>
    public static TraceabilityResult ComputeTraceability(
        string reportText,
        IReadOnlySet<string> validTxnIds,
        IReadOnlySet<string>? goldTxnIds)
    {
        var cited = ExtractCitedTxnIds(reportText);
        var citedDistinct = new HashSet<string>(cited, StringComparer.OrdinalIgnoreCase);
        var fabricated = citedDistinct
            .Where(id => !validTxnIds.Contains(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var grounded = new HashSet<string>(
            citedDistinct.Where(id => validTxnIds.Contains(id)),
            StringComparer.OrdinalIgnoreCase);
        var groundedList = grounded.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

        int matched = 0;
        double? precision = null, recall = null, f1 = null;
        var matchedList = new List<string>();
        var missingList = new List<string>();
        var goldList = goldTxnIds is null
            ? new List<string>()
            : goldTxnIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

        if (goldTxnIds is not null)
        {
            matchedList = grounded.Where(id => goldTxnIds.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            missingList = goldTxnIds.Where(id => !grounded.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            matched = matchedList.Count;
            precision = grounded.Count == 0 ? null : Math.Round((double)matched / grounded.Count, 4);
            recall = goldTxnIds.Count == 0 ? null : Math.Round((double)matched / goldTxnIds.Count, 4);
            if (precision is double p && recall is double r && (p + r) > 0)
                f1 = Math.Round(2 * p * r / (p + r), 4);
        }

        return new TraceabilityResult(
            CitedTotal: cited.Count,
            CitedDistinct: citedDistinct.Count,
            FabricatedCitations: fabricated,
            GroundedDistinct: grounded.Count,
            GroundedCitations: groundedList,
            GoldTotal: goldTxnIds?.Count,
            GoldEvidenceTxnIds: goldList,
            MatchedGoldCitations: matched,
            MatchedGoldCitationsList: matchedList,
            MissingGoldCitationsList: missingList,
            Precision: precision,
            Recall: recall,
            F1: f1);
    }

    /// <summary>
    /// Evidence-Grounded Hallucination Rate: proportion of atomic claims
    /// that are unsupported (extrinsic) or contradicted (intrinsic) by the
    /// case evidence, per the Ji et al. (2023) taxonomy the proposal adopts.
    /// Claim text and an initial support label come from the LLM judge;
    /// this method is the deterministic backstop that forces any claim
    /// citing a nonexistent transaction ID to "unsupported" regardless of
    /// what the LLM said, so the judge cannot inflate its own grounding.
    /// </summary>
    public static EghrResult ScoreClaims(IEnumerable<ClaimInput> claims, IReadOnlySet<string> validTxnIds)
    {
        var results = new List<ClaimResult>();
        int supported = 0, unsupported = 0, contradicted = 0;

        foreach (var claim in claims)
        {
            var citedIds = claim.CitedTxnIds ?? Array.Empty<string>();
            var fabricated = citedIds.Any(id => !validTxnIds.Contains(id));
            var support = (claim.Support ?? "unsupported").Trim().ToLowerInvariant();

            if (fabricated || support is not ("supported" or "contradicted"))
                support = "unsupported";

            switch (support)
            {
                case "supported": supported++; break;
                case "contradicted": contradicted++; break;
                default: unsupported++; break;
            }

            results.Add(new ClaimResult(claim.Text, citedIds, support, fabricated));
        }

        int total = results.Count;
        double rate = total == 0 ? 0.0 : Math.Round((double)(unsupported + contradicted) / total, 4);

        return new EghrResult(total, supported, unsupported, contradicted, rate, results);
    }
}

/// <summary>A single claim as extracted by the LLM judge, before deterministic scoring.</summary>
public sealed record ClaimInput(string Text, IReadOnlyList<string> CitedTxnIds, string Support);

/// <summary>A claim after the deterministic citation-existence override has been applied.</summary>
public sealed record ClaimResult(string Text, IReadOnlyList<string> CitedTxnIds, string Support, bool FabricatedCitation);

/// <summary>Evidence-Grounded Hallucination Rate for one judged report.</summary>
public sealed record EghrResult(
    int TotalClaims,
    int SupportedCount,
    int UnsupportedCount,
    int ContradictedCount,
    double Rate,
    IReadOnlyList<ClaimResult> Claims);

/// <summary>Citation precision/recall/F1 of a report's evidence traceability.</summary>
public sealed record TraceabilityResult(
    int CitedTotal,
    int CitedDistinct,
    IReadOnlyList<string> FabricatedCitations,
    int GroundedDistinct,
    IReadOnlyList<string> GroundedCitations,
    int? GoldTotal,
    IReadOnlyList<string> GoldEvidenceTxnIds,
    int MatchedGoldCitations,
    IReadOnlyList<string> MatchedGoldCitationsList,
    IReadOnlyList<string> MissingGoldCitationsList,
    double? Precision,
    double? Recall,
    double? F1);
