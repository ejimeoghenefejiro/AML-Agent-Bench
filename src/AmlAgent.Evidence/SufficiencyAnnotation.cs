using System.Text.Json.Nodes;

namespace AmlAgent.Evidence;

/// <summary>
/// Schema + strict reader for human sufficiency annotations (fix #8) --
/// the "Sufficiency" decision in docs/evidence-annotation-protocol.md's
/// annotation-decisions table: whether a claim's evidence is *adequate for
/// the scope and strength of the claim*, not merely relevant or present.
/// This is a distinct annotation unit from support/necessity (see
/// AmlAgent.Evidence.Claim/ReferenceEvidence, which already capture what
/// evidence a claim cites and what's required/acceptable) -- a claim can
/// have perfectly valid, gold-matching evidence and still be judged
/// insufficient (e.g. one leg of a three-hop transfer chain establishing
/// only part of a layering claim) or overbroad (evidence supports a
/// narrower claim than the one actually made).
///
/// Deliberately schema-and-fixtures only, per the PhD's stated sequencing:
/// Claim Support Coverage (fix #7, deterministic set comparison) was built
/// first because it needs no semantic judgement beyond the LLM identifying
/// citations; Evidence Sufficiency Rate is harder -- "is this evidence
/// adequate" is inherently a human/expert judgement call, not something a
/// deterministic rule or an unvalidated LLM should decide on its own. This
/// type exists so a real annotation round has a schema and a loader ready
/// the moment it happens; nothing in this file computes evidence_sufficiency_rate
/// or any other scored value, and nothing calls this reader from the live
/// judge or assurance-profile pipeline (see EvidenceTraceabilityProfileBuilder.Build,
/// where evidence_sufficiency_rate stays an explicit null -- unchanged by
/// this fix, deliberately). See docs/evidence-annotation-protocol.md
/// #evidence-sufficiency-annotation-schema and
/// validation/gold/sufficiency/README.md for the human-facing version of
/// this same explanation.
/// </summary>
/// <summary>
/// CaseId/OutputId deliberately mirror AmlAgent.Evidence.HumanAnnotationSet's
/// scoping, not just a task id: sufficiency (like support) is a judgement
/// about one specific candidate output's cited evidence for a claim, not an
/// abstract property of the claim in isolation -- the same evidence set could
/// be sufficient for a cautiously-worded claim and insufficient for an
/// overbroad one, so "is this claim's evidence sufficient" only has an
/// answer relative to a specific report making a specific claim.
/// </summary>
public sealed record SufficiencyAnnotationSet(string CaseId, string OutputId, IReadOnlyList<SufficiencyAnnotator> Annotators);

public sealed record SufficiencyAnnotator(string AnnotatorId, IReadOnlyList<ClaimSufficiencyJudgement> Judgements);

/// <summary>
/// One annotator's sufficiency judgement for one claim.
/// SufficiencyLabel is one of "sufficient" / "insufficient" / "overbroad"
/// (validated by the reader, not left as an arbitrary free string) --
/// "overbroad" is the case the annotation-decisions table's question 6 names
/// explicitly: a claim can be true but broader than what the available
/// evidence can actually establish. MinimumSufficientEvidenceSets is the
/// annotator's answer to "what is the smallest evidence set that would make
/// this claim adequately supported" -- optional, since an annotator judging
/// a claim already-sufficient may not need to construct a counterfactual
/// minimal set.
/// </summary>
public sealed record ClaimSufficiencyJudgement(
    string ClaimId,
    string SufficiencyLabel,
    IReadOnlyList<IReadOnlyList<string>>? MinimumSufficientEvidenceSets,
    string? Rationale);

/// <summary>
/// Parses a sufficiency-annotation JSON file. No annotation data is ever
/// fabricated by this reader or by anything that calls it -- it only ever
/// returns what was actually present in the file, and throws a clear,
/// specific error for anything malformed rather than guessing or defaulting.
/// Mirrors AmlAgent.Evidence.HumanAnnotationReader's shape and strictness
/// deliberately, since both read the same class of not-yet-collected human
/// annotation data.
/// </summary>
public static class SufficiencyAnnotationReader
{
    private static readonly string[] ValidLabels = { "sufficient", "insufficient", "overbroad" };

    public static SufficiencyAnnotationSet Parse(string json, string? sourcePathForErrors = null)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (Exception ex) { throw new InvalidSufficiencyAnnotationException($"invalid JSON{Suffix(sourcePathForErrors)}: {ex.Message}"); }

        var obj = root as JsonObject
            ?? throw new InvalidSufficiencyAnnotationException($"sufficiency annotation file{Suffix(sourcePathForErrors)} must be a JSON object");

        var caseId = (string?)obj["case_id"];
        if (string.IsNullOrWhiteSpace(caseId))
            throw new InvalidSufficiencyAnnotationException($"sufficiency annotation file{Suffix(sourcePathForErrors)} is missing required field 'case_id'");

        var outputId = (string?)obj["output_id"];
        if (string.IsNullOrWhiteSpace(outputId))
            throw new InvalidSufficiencyAnnotationException($"sufficiency annotation file{Suffix(sourcePathForErrors)} is missing required field 'output_id'");

        var annotatorsNode = obj["annotators"] as JsonArray;
        if (annotatorsNode is null || annotatorsNode.Count == 0)
            throw new InvalidSufficiencyAnnotationException($"sufficiency annotation file{Suffix(sourcePathForErrors)} must have a non-empty 'annotators' array");

        var annotators = new List<SufficiencyAnnotator>();
        for (int i = 0; i < annotatorsNode.Count; i++)
        {
            if (annotatorsNode[i] is not JsonObject annotatorObj)
                throw new InvalidSufficiencyAnnotationException($"annotators[{i}]{Suffix(sourcePathForErrors)} must be a JSON object");

            var annotatorId = (string?)annotatorObj["annotator_id"];
            if (string.IsNullOrWhiteSpace(annotatorId))
                throw new InvalidSufficiencyAnnotationException($"annotators[{i}]{Suffix(sourcePathForErrors)} is missing required field 'annotator_id'");

            var judgementsNode = annotatorObj["claim_sufficiency"] as JsonArray ?? new JsonArray();
            var judgements = new List<ClaimSufficiencyJudgement>();
            for (int j = 0; j < judgementsNode.Count; j++)
            {
                if (judgementsNode[j] is not JsonObject judgementObj)
                    throw new InvalidSufficiencyAnnotationException($"annotators[{i}].claim_sufficiency[{j}]{Suffix(sourcePathForErrors)} must be a JSON object");

                var claimId = (string?)judgementObj["claim_id"];
                if (string.IsNullOrWhiteSpace(claimId))
                    throw new InvalidSufficiencyAnnotationException($"annotators[{i}].claim_sufficiency[{j}]{Suffix(sourcePathForErrors)} is missing required field 'claim_id'");

                var label = (string?)judgementObj["sufficiency_label"];
                if (string.IsNullOrWhiteSpace(label))
                    throw new InvalidSufficiencyAnnotationException($"annotators[{i}].claim_sufficiency[{j}] ('{claimId}'){Suffix(sourcePathForErrors)} is missing required field 'sufficiency_label'");
                if (!ValidLabels.Contains(label))
                    throw new InvalidSufficiencyAnnotationException($"annotators[{i}].claim_sufficiency[{j}] ('{claimId}'){Suffix(sourcePathForErrors)} has invalid sufficiency_label '{label}' -- must be one of: {string.Join(", ", ValidLabels)}");

                var minimumSets = judgementObj["minimum_sufficient_evidence_sets"]?.AsArray()?
                    .Select(set => (IReadOnlyList<string>)(set?.AsArray()?.Select(n => (string)n!).ToList() ?? new List<string>()))
                    .ToList();
                var rationale = (string?)judgementObj["rationale"];

                judgements.Add(new ClaimSufficiencyJudgement(claimId, label, minimumSets, rationale));
            }

            annotators.Add(new SufficiencyAnnotator(annotatorId, judgements));
        }

        return new SufficiencyAnnotationSet(caseId, outputId, annotators);
    }

    private static string Suffix(string? sourcePath) => sourcePath is null ? "" : $" ({sourcePath})";
}

public sealed class InvalidSufficiencyAnnotationException : Exception
{
    public InvalidSufficiencyAnnotationException(string message) : base(message) { }
}
