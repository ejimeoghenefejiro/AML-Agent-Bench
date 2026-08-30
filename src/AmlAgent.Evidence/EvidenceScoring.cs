using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AmlAgent.Evidence;

/// <summary>
/// Pure, dependency-free scoring logic underlying evidence traceability
/// (citation precision/recall) — the PhD's sole primary metric — and the
/// legacy Evidence-Grounded Hallucination Rate (EGHR) claim-support check,
/// retained as a secondary metric (see
/// docs/evidence-traceability-framework.md#legacy-eghr-metric). No LLM,
/// network or file I/O happens in this class, so it is directly
/// unit-testable without a workspace or an OPENAI_API_KEY.
///
/// Citation-existence checking (is a cited transaction ID real?) is always
/// deterministic here — callers (e.g. the LLM-as-judge) cannot mark a
/// fabricated citation as "supported".
/// </summary>
public static class EvidenceScoring
{
    private static readonly Regex TxnIdPattern = new(@"\bT[123]-\d{3}\b", RegexOptions.Compiled);

    /// <summary>Whole-token candidate ids in free text: starts with a letter/digit, continues with letters/digits/hyphen/underscore. Used by ExtractCitedEvidenceIds to tokenise a report once rather than re-scanning per known id.</summary>
    private static readonly Regex TokenPattern = new(@"[A-Za-z0-9][A-Za-z0-9_\-]*", RegexOptions.Compiled);

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
        IReadOnlySet<string>? goldTxnIds) =>
        ComputeTraceabilityFromCitations(ExtractCitedTxnIds(reportText), validTxnIds, goldTxnIds);

    /// <summary>
    /// Every known evidence id (any type -- transaction, relationship, SAR,
    /// watchlist entry, whatever the case actually contains) that appears in
    /// free text as a whole token, including duplicates. Tokenises the text
    /// once (O(text length)) and looks each token up in a hashset of known
    /// ids (O(1) per token), rather than running one regex per evidence item
    /// -- correctness and performance both come from matching against the
    /// case's ACTUAL id vocabulary instead of a fixed shape pattern, which is
    /// what lets a relationship id like "R1" or a SAR id like "SAR-2026-001"
    /// be recognised for the first time. The returned strings are the
    /// reference's own casing (EvidenceId), not necessarily the text's.
    /// </summary>
    public static List<string> ExtractCitedEvidenceIds(string text, IReadOnlyCollection<EvidenceReference> knownEvidence)
    {
        var cited = new List<string>();
        if (string.IsNullOrEmpty(text) || knownEvidence.Count == 0) return cited;

        var knownIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in knownEvidence)
            knownIds[reference.EvidenceId] = reference.EvidenceId;

        foreach (Match m in TokenPattern.Matches(text))
        {
            if (knownIds.TryGetValue(m.Value, out var canonicalId))
                cited.Add(canonicalId);
        }
        return cited;
    }

    /// <summary>
    /// Infers a regex "shape" per known evidence id, so a fabricated id of a
    /// type actually present in the case can be recognised even though it
    /// was never seen before (fix #3 of the v0.3 validation-priorities pass,
    /// closing the gap ComputeTraceability's own doc comment used to flag as
    /// "still open"). The shape is derived by replacing every maximal run of
    /// digits in the id with a digit-run placeholder and escaping everything
    /// else -- e.g. "T1-001"/"T2-014" both produce the shape "T\d+-\d+";
    /// "R1".."R7" produce "R\d+"; "WATCHLIST1" produces "WATCHLIST\d+". An id
    /// with no digits at all produces no shape (nothing to generalise from a
    /// single literal token) and is only ever matched by its own exact value,
    /// via ExtractCitedEvidenceIds. This is inherently a heuristic, the same
    /// way the original hardcoded transaction regex always was: a shape
    /// derived from real ids can, in principle, coincidentally match an
    /// ordinary word with trailing digits that was never meant as a
    /// citation. That tradeoff is deliberate and consistent with this
    /// benchmark's existing fabrication-detection design -- an
    /// evidence-shaped token that doesn't exist is treated as a citation
    /// attempt worth flagging, not silently ignored.
    /// </summary>
    public static IReadOnlyList<Regex> InferEvidenceIdShapes(IReadOnlyCollection<EvidenceReference> knownEvidence)
    {
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in knownEvidence)
        {
            var escaped = Regex.Escape(reference.EvidenceId);
            var shape = DigitRun.Replace(escaped, @"\d+");
            if (shape != escaped) // at least one digit run was found and generalised
                shapes.Add(shape);
        }
        return shapes.Select(s => new Regex($"^{s}$", RegexOptions.Compiled)).ToList();
    }

    private static readonly Regex DigitRun = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// Every token in free text that LOOKS like a real evidence id of some
    /// type actually present in validEvidence (matches an InferEvidenceIdShapes
    /// pattern) but isn't one of the known ids and isn't already caught by the
    /// legacy transaction-id-shaped regex (excluded explicitly here so
    /// ComputeTraceability's union below never counts one text occurrence
    /// twice). This generalises transaction-only fabrication detection (fix
    /// #1's "T3-999 is caught, a fabricated relationship id is not" gap) to
    /// any evidence type the case actually has real examples of.
    /// </summary>
    public static List<string> ExtractShapeFabricatedIds(string text, IReadOnlyCollection<EvidenceReference> knownEvidence)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text) || knownEvidence.Count == 0) return result;

        var shapes = InferEvidenceIdShapes(knownEvidence);
        if (shapes.Count == 0) return result;

        var knownIds = new HashSet<string>(knownEvidence.Select(e => e.EvidenceId), StringComparer.OrdinalIgnoreCase);

        foreach (Match m in TokenPattern.Matches(text))
        {
            if (knownIds.Contains(m.Value)) continue;
            if (TxnIdPattern.IsMatch(m.Value)) continue; // legacy txn-shape path already accounts for this occurrence
            if (shapes.Any(s => s.IsMatch(m.Value)))
                result.Add(m.Value);
        }
        return result;
    }

    /// <summary>
    /// Generalised evidence traceability: citation precision/recall against a
    /// gold evidence set drawn from ANY evidence type (see EvidenceReference),
    /// not just transactions. Extraction unions three mechanisms, each
    /// covering a disjoint set of token occurrences so no single occurrence
    /// in the text is ever counted twice: (1) known-evidence-id token
    /// matching, which recognises a real citation to any evidence type
    /// actually present in validEvidence regardless of its id shape; (2) the
    /// legacy transaction-id-shaped regex, restricted to ids NOT already
    /// found by (1), so a fabricated transaction-shaped id (e.g. "T3-999") is
    /// caught as fabricated exactly as the transaction-only overload below
    /// does; (3) ExtractShapeFabricatedIds (fix #3 of the v0.3
    /// validation-priorities pass), which generalises (2) to every evidence
    /// type the case actually contains real examples of -- a fabricated
    /// relationship id like "R99" (when the case has real R1..R7) or a
    /// fabricated watchlist id are now caught too, not just transaction-
    /// shaped fabrications. (3) explicitly excludes anything (2) already
    /// matches, so the two never double-count the same occurrence. See
    /// docs/evidence-traceability-framework.md#generic-fabricated-evidence-detection
    /// for what this still doesn't catch (an evidence type with no real
    /// examples anywhere in validEvidence has no shape to infer, and a
    /// fabrication that happens to look like ordinary English prose with no
    /// digits at all is inherently unrecognisable as a citation attempt).
    /// </summary>
    public static TraceabilityResult ComputeTraceability(
        string reportText,
        IReadOnlyCollection<EvidenceReference> validEvidence,
        IReadOnlyCollection<EvidenceReference>? goldEvidence)
    {
        var validIds = new HashSet<string>(validEvidence.Select(e => e.EvidenceId), StringComparer.OrdinalIgnoreCase);
        var goldIds = goldEvidence is null ? null : new HashSet<string>(goldEvidence.Select(e => e.EvidenceId), StringComparer.OrdinalIgnoreCase);

        var cited = new List<string>();
        cited.AddRange(ExtractCitedEvidenceIds(reportText, validEvidence));
        cited.AddRange(ExtractCitedTxnIds(reportText).Where(id => !validIds.Contains(id)));
        cited.AddRange(ExtractShapeFabricatedIds(reportText, validEvidence));

        return ComputeTraceabilityFromCitations(cited, validIds, goldIds);
    }

    /// <summary>Shared arithmetic between the transaction-only and generalised ComputeTraceability overloads, so the two stay behaviourally identical wherever their inputs are equivalent.</summary>
    private static TraceabilityResult ComputeTraceabilityFromCitations(List<string> cited, IReadOnlySet<string> validIds, IReadOnlySet<string>? goldIds)
    {
        var citedDistinct = new HashSet<string>(cited, StringComparer.OrdinalIgnoreCase);
        var fabricated = citedDistinct
            .Where(id => !validIds.Contains(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var grounded = new HashSet<string>(
            citedDistinct.Where(id => validIds.Contains(id)),
            StringComparer.OrdinalIgnoreCase);
        var groundedList = grounded.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

        int matched = 0;
        double? precision = null, recall = null, f1 = null;
        double? validEvidencePrecision = null, validEvidenceF1 = null;
        var matchedList = new List<string>();
        var missingList = new List<string>();
        var goldList = goldIds is null
            ? new List<string>()
            : goldIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

        if (goldIds is not null)
        {
            matchedList = grounded.Where(id => goldIds.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            missingList = goldIds.Where(id => !grounded.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            matched = matchedList.Count;

            // Standard IR precision: matched / ALL distinct cited evidence,
            // fabricated citations included in the denominator (matches the
            // framework's own EP_i = |E_i^agent ∩ E_i*| / |E_i^agent| formula,
            // where E_i^agent is everything cited, not a pre-filtered subset).
            // Defined (not null) even when every citation is fabricated -- a
            // report citing nothing real correctly scores 0.0, not "N/A".
            precision = citedDistinct.Count == 0 ? null : Math.Round((double)matched / citedDistinct.Count, 4);
            recall = goldIds.Count == 0 ? null : Math.Round((double)matched / goldIds.Count, 4);
            if (precision is double p && recall is double r && (p + r) > 0)
                f1 = Math.Round(2 * p * r / (p + r), 4);

            // Valid-Evidence Precision: the metric's original formula,
            // preserved under an explicit name -- matched / grounded (real)
            // citations only, so fabrication is invisible to it by design.
            // Null (not zero) when there are no valid citations to form a
            // ratio over at all, distinguishing "no real evidence cited" from
            // "real evidence cited but none of it was gold-relevant" (0.0).
            validEvidencePrecision = grounded.Count == 0 ? null : Math.Round((double)matched / grounded.Count, 4);
            if (validEvidencePrecision is double vep && recall is double vr && (vep + vr) > 0)
                validEvidenceF1 = Math.Round(2 * vep * vr / (vep + vr), 4);
        }

        return new TraceabilityResult(
            CitedTotal: cited.Count,
            CitedDistinct: citedDistinct.Count,
            FabricatedCitations: fabricated,
            GroundedDistinct: grounded.Count,
            GroundedCitations: groundedList,
            GoldTotal: goldIds?.Count,
            GoldEvidenceTxnIds: goldList,
            MatchedGoldCitations: matched,
            MatchedGoldCitationsList: matchedList,
            MissingGoldCitationsList: missingList,
            Precision: precision,
            Recall: recall,
            F1: f1,
            ValidEvidencePrecision: validEvidencePrecision,
            ValidEvidenceF1: validEvidenceF1);
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

/// <summary>
/// Citation precision/recall/F1 of a report's evidence traceability.
///
/// Precision has two defensible denominators when a report cites a
/// fabricated id alongside real ones, and this type reports both rather than
/// picking one silently (see docs/evidence-traceability-framework.md
/// #evidence-precision-ep):
///   - <see cref="Precision"/> / <see cref="F1"/>: standard IR definition,
///     matched-gold over ALL distinct cited evidence (fabricated citations
///     count against the denominator). This is the framework's primary
///     reported metric and matches its own formal EP_i definition.
///   - <see cref="ValidEvidencePrecision"/> / <see cref="ValidEvidenceF1"/>:
///     matched-gold over grounded (real) citations only, so fabrication is
///     invisible to it by design -- conditional on "given the citations that
///     are real, how precise were they". Kept for anyone who wants precision
///     reported independently of fabrication (which is separately visible via
///     <see cref="FabricatedCitations"/>).
/// Recall is unaffected either way: fabricated citations can never be gold,
/// so they never change the recall denominator (goldIds) or numerator
/// (matched).
/// </summary>
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
    double? F1,
    double? ValidEvidencePrecision = null,
    double? ValidEvidenceF1 = null);
