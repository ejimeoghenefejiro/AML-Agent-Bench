using System.Text.Json.Nodes;

namespace AmlAgent.Evidence;

/// <summary>
/// Schema + strict reader for one annotator's INDEPENDENT gold material-claim
/// derivation for a task's case (v0.3 validation-priorities item 1) -- a
/// different annotation target from AmlAgent.Evidence.HumanAnnotation, which
/// reviews a specific candidate agent's REPORT. This one has no candidate
/// output in the loop at all: an annotator works from the frozen annotator
/// package (case data + instructions, no author answers -- see
/// validation/annotator-packages/task-007-v1/README.md) and independently
/// writes down what they believe the material claims are and what evidence
/// each one needs, in the same Required/AcceptableAlternatives/Corroborating
/// shape task-007's evidence-annotations.json already uses
/// (AmlAgent.Evidence.ReferenceEvidence). Comparing two or more annotators'
/// independent GoldClaimAnnotationSets against each other (not against the
/// original single-author file) is what lets task-007's gold evidence move
/// from single-author to genuinely multi-annotator -- see
/// AmlAgent.Evidence.ClaimAnnotationAdjudication for the comparison/merge
/// logic and AgreementStatistics for the chance-corrected agreement measures.
/// </summary>
public sealed record GoldClaimAnnotationSet(
    string SchemaVersion,
    string TaskId,
    string AnnotatorId,
    string AdjudicationStatus,
    IReadOnlyList<GoldClaimAnnotation> Claims);

/// <summary>One annotator's independently-derived claim: their own claim_id/text plus their own Required/AcceptableAlternatives/Corroborating judgement, with a free-text rationale (the annotation-decisions table's own "why" -- see docs/evidence-annotation-protocol.md).</summary>
public sealed record GoldClaimAnnotation(
    string ClaimId,
    string Text,
    IReadOnlyList<string> Required,
    IReadOnlyList<IReadOnlyList<string>>? AcceptableAlternatives,
    IReadOnlyList<string>? Corroborating,
    string? Rationale);

/// <summary>
/// Parses a gold-claim-annotation JSON file. No annotation data is ever
/// fabricated by this reader or by anything that calls it -- it only ever
/// returns what was actually present in the file, and throws a clear,
/// specific error for anything malformed rather than guessing or defaulting.
/// Mirrors AmlAgent.Evidence.SufficiencyAnnotationReader/HumanAnnotationReader's
/// shape and strictness deliberately, since all three read the same class of
/// externally-authored human annotation data.
/// </summary>
public static class GoldClaimAnnotationReader
{
    /// <summary>adjudication_status values docs/evidence-annotation-protocol.md#annotation-provenance already names.</summary>
    public static readonly string[] ValidAdjudicationStatuses = { "draft", "single-annotator", "adjudicated", "multi-annotator-validated" };

    public const string CurrentSchemaVersion = "1.0";

    public static GoldClaimAnnotationSet Parse(string json, string? sourcePathForErrors = null)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (Exception ex) { throw new InvalidGoldClaimAnnotationException($"invalid JSON{Suffix(sourcePathForErrors)}: {ex.Message}"); }

        var obj = root as JsonObject
            ?? throw new InvalidGoldClaimAnnotationException($"gold claim annotation file{Suffix(sourcePathForErrors)} must be a JSON object");

        var schemaVersion = (string?)obj["schema_version"];
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new InvalidGoldClaimAnnotationException($"gold claim annotation file{Suffix(sourcePathForErrors)} is missing required field 'schema_version'");

        var taskId = (string?)obj["task_id"];
        if (string.IsNullOrWhiteSpace(taskId))
            throw new InvalidGoldClaimAnnotationException($"gold claim annotation file{Suffix(sourcePathForErrors)} is missing required field 'task_id'");

        var annotatorId = (string?)obj["annotator_id"];
        if (string.IsNullOrWhiteSpace(annotatorId))
            throw new InvalidGoldClaimAnnotationException($"gold claim annotation file{Suffix(sourcePathForErrors)} is missing required field 'annotator_id'");

        var adjudicationStatus = (string?)obj["adjudication_status"];
        if (string.IsNullOrWhiteSpace(adjudicationStatus))
            throw new InvalidGoldClaimAnnotationException($"gold claim annotation file{Suffix(sourcePathForErrors)} is missing required field 'adjudication_status'");
        if (!ValidAdjudicationStatuses.Contains(adjudicationStatus))
            throw new InvalidGoldClaimAnnotationException($"gold claim annotation file{Suffix(sourcePathForErrors)} has invalid adjudication_status '{adjudicationStatus}' -- must be one of: {string.Join(", ", ValidAdjudicationStatuses)}");

        var claimsNode = obj["claims"] as JsonArray;
        if (claimsNode is null || claimsNode.Count == 0)
            throw new InvalidGoldClaimAnnotationException($"gold claim annotation file{Suffix(sourcePathForErrors)} must have a non-empty 'claims' array");

        var claims = new List<GoldClaimAnnotation>();
        for (int i = 0; i < claimsNode.Count; i++)
        {
            if (claimsNode[i] is not JsonObject claimObj)
                throw new InvalidGoldClaimAnnotationException($"claims[{i}]{Suffix(sourcePathForErrors)} must be a JSON object");

            var claimId = (string?)claimObj["claim_id"];
            if (string.IsNullOrWhiteSpace(claimId))
                throw new InvalidGoldClaimAnnotationException($"claims[{i}]{Suffix(sourcePathForErrors)} is missing required field 'claim_id'");

            var text = (string?)claimObj["text"];
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidGoldClaimAnnotationException($"claims[{i}] ('{claimId}'){Suffix(sourcePathForErrors)} is missing required field 'text'");

            var required = claimObj["required"]?.AsArray()?.Select(n => (string)n!).ToList()
                ?? throw new InvalidGoldClaimAnnotationException($"claims[{i}] ('{claimId}'){Suffix(sourcePathForErrors)} is missing required field 'required'");

            var alternatives = claimObj["acceptable_alternatives"]?.AsArray()?
                .Select(alt => (IReadOnlyList<string>)(alt?.AsArray()?.Select(n => (string)n!).ToList() ?? new List<string>()))
                .ToList();
            var corroborating = claimObj["corroborating"]?.AsArray()?.Select(n => (string)n!).ToList();
            var rationale = (string?)claimObj["rationale"];

            claims.Add(new GoldClaimAnnotation(claimId, text, required, alternatives, corroborating, rationale));
        }

        return new GoldClaimAnnotationSet(schemaVersion, taskId, annotatorId, adjudicationStatus, claims);
    }

    private static string Suffix(string? sourcePath) => sourcePath is null ? "" : $" ({sourcePath})";
}

public sealed class InvalidGoldClaimAnnotationException : Exception
{
    public InvalidGoldClaimAnnotationException(string message) : base(message) { }
}
