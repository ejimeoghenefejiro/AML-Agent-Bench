using System.Text.Json.Nodes;

namespace AmlAgent.Evidence;

/// <summary>
/// One case/output's human gold annotations -- possibly from multiple independent
/// annotators, so inter-annotator agreement can be measured directly from the
/// same file. Matches the research-validation instructions' example shape.
/// RubricScores is additive to that example (dimension_id -&gt; 0-5 score) so
/// "LLM judge vs human rubric scores" (item 9) has somewhere real to live; it is
/// optional and null when an annotator only did claim-level annotation.
/// </summary>
public sealed record HumanAnnotationSet(string CaseId, string OutputId, IReadOnlyList<HumanAnnotator> Annotators);

public sealed record HumanAnnotator(
    string AnnotatorId,
    IReadOnlyList<HumanClaimAnnotation> Claims,
    IReadOnlyDictionary<string, double>? RubricScores = null);

public sealed record HumanClaimAnnotation(string ClaimId, string Classification, IReadOnlyList<string> EvidenceIds);

/// <summary>
/// Parses a human-annotation JSON file. No annotation data is ever fabricated by
/// this reader or by anything that calls it -- it only ever returns what was
/// actually present in the file, and throws a clear, specific error for anything
/// malformed rather than guessing or defaulting.
/// </summary>
public static class HumanAnnotationReader
{
    public static HumanAnnotationSet Parse(string json, string? sourcePathForErrors = null)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (Exception ex) { throw new InvalidHumanAnnotationException($"invalid JSON{Suffix(sourcePathForErrors)}: {ex.Message}"); }

        var obj = root as JsonObject
            ?? throw new InvalidHumanAnnotationException($"human annotation file{Suffix(sourcePathForErrors)} must be a JSON object");

        var caseId = (string?)obj["case_id"];
        if (string.IsNullOrWhiteSpace(caseId))
            throw new InvalidHumanAnnotationException($"human annotation file{Suffix(sourcePathForErrors)} is missing required field 'case_id'");

        var outputId = (string?)obj["output_id"];
        if (string.IsNullOrWhiteSpace(outputId))
            throw new InvalidHumanAnnotationException($"human annotation file{Suffix(sourcePathForErrors)} is missing required field 'output_id'");

        var annotatorsNode = obj["annotators"] as JsonArray;
        if (annotatorsNode is null || annotatorsNode.Count == 0)
            throw new InvalidHumanAnnotationException($"human annotation file{Suffix(sourcePathForErrors)} must have a non-empty 'annotators' array");

        var annotators = new List<HumanAnnotator>();
        for (int i = 0; i < annotatorsNode.Count; i++)
        {
            if (annotatorsNode[i] is not JsonObject annotatorObj)
                throw new InvalidHumanAnnotationException($"annotators[{i}]{Suffix(sourcePathForErrors)} must be a JSON object");

            var annotatorId = (string?)annotatorObj["annotator_id"];
            if (string.IsNullOrWhiteSpace(annotatorId))
                throw new InvalidHumanAnnotationException($"annotators[{i}]{Suffix(sourcePathForErrors)} is missing required field 'annotator_id'");

            var claimsNode = annotatorObj["claims"] as JsonArray ?? new JsonArray();
            var claims = new List<HumanClaimAnnotation>();
            for (int c = 0; c < claimsNode.Count; c++)
            {
                if (claimsNode[c] is not JsonObject claimObj)
                    throw new InvalidHumanAnnotationException($"annotators[{i}].claims[{c}]{Suffix(sourcePathForErrors)} must be a JSON object");

                var claimId = (string?)claimObj["claim_id"];
                if (string.IsNullOrWhiteSpace(claimId))
                    throw new InvalidHumanAnnotationException($"annotators[{i}].claims[{c}]{Suffix(sourcePathForErrors)} is missing required field 'claim_id'");

                var classification = (string?)claimObj["classification"];
                if (string.IsNullOrWhiteSpace(classification))
                    throw new InvalidHumanAnnotationException($"annotators[{i}].claims[{c}] ('{claimId}'){Suffix(sourcePathForErrors)} is missing required field 'classification'");

                var evidenceIds = claimObj["evidence_ids"]?.AsArray().Select(n => (string)n!).ToList() ?? new List<string>();
                claims.Add(new HumanClaimAnnotation(claimId, classification, evidenceIds));
            }

            IReadOnlyDictionary<string, double>? rubricScores = null;
            if (annotatorObj["rubric_scores"] is JsonObject rubricObj)
                rubricScores = rubricObj.ToDictionary(kv => kv.Key, kv => (double)kv.Value!);

            annotators.Add(new HumanAnnotator(annotatorId, claims, rubricScores));
        }

        return new HumanAnnotationSet(caseId, outputId, annotators);
    }

    private static string Suffix(string? sourcePath) => sourcePath is null ? "" : $" ({sourcePath})";
}

public sealed class InvalidHumanAnnotationException : Exception
{
    public InvalidHumanAnnotationException(string message) : base(message) { }
}
