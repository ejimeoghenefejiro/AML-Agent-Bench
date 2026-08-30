namespace AmlAgent.Evidence;

/// <summary>
/// Compares two or more independent annotators' GoldClaimAnnotationSets for
/// the same task (v0.3 validation-priorities item 1) -- surfaces exactly
/// where they agree and disagree, so a human adjudicator has a concrete
/// worksheet rather than having to diff raw JSON by hand. Every method here
/// returns raw comparison data, never a single "they agree" verdict --
/// consistent with AmlAgent.Evidence.JudgeVsHumanComparison's own discipline
/// (see AmlAgent.Evidence.AgreementStatistics for the separate, explicit
/// chance-corrected agreement computation once the raw picture is available).
/// </summary>
public static class ClaimAnnotationAdjudication
{
    /// <summary>
    /// Compares two annotators' claim sets by claim_id: which claims only one
    /// of them identified at all (itself a disagreement worth surfacing, not
    /// just a claim-level Required/AcceptableAlternatives difference), and for
    /// claims both identified, whether their Required sets match exactly.
    /// AcceptableAlternatives/Corroborating differences are reported but do
    /// not by themselves make a claim "disagreed" -- Required is the
    /// annotation-decisions table's "Necessity" question, the one two
    /// annotators most need to converge on; alternatives are additive.
    /// </summary>
    public static ClaimSetComparison Compare(GoldClaimAnnotationSet a, GoldClaimAnnotationSet b)
    {
        var aById = a.Claims.ToDictionary(c => c.ClaimId, StringComparer.OrdinalIgnoreCase);
        var bById = b.Claims.ToDictionary(c => c.ClaimId, StringComparer.OrdinalIgnoreCase);

        var onlyInA = aById.Keys.Except(bById.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var onlyInB = bById.Keys.Except(aById.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var comparedIds = aById.Keys.Intersect(bById.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var perClaim = new List<ClaimComparison>();
        foreach (var claimId in comparedIds)
        {
            var claimA = aById[claimId];
            var claimB = bById[claimId];

            var requiredA = new HashSet<string>(claimA.Required, StringComparer.OrdinalIgnoreCase);
            var requiredB = new HashSet<string>(claimB.Required, StringComparer.OrdinalIgnoreCase);

            bool requiredMatches = requiredA.SetEquals(requiredB);
            var requiredOnlyInA = requiredA.Except(requiredB, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var requiredOnlyInB = requiredB.Except(requiredA, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();

            perClaim.Add(new ClaimComparison(claimId, requiredMatches, requiredOnlyInA, requiredOnlyInB,
                claimA.AcceptableAlternatives?.Count ?? 0, claimB.AcceptableAlternatives?.Count ?? 0));
        }

        return new ClaimSetComparison(comparedIds.Count, perClaim.Count(c => c.RequiredMatches), perClaim, onlyInA, onlyInB);
    }

    /// <summary>
    /// Merges two or more annotators' raw claim sets and an adjudicator's
    /// explicit resolutions into a single final GoldClaimAnnotationSet marked
    /// adjudication_status "adjudicated" -- the adjudicator's own decisions
    /// are the only source of the merged Required/AcceptableAlternatives sets
    /// (this method never auto-resolves a disagreement by picking one
    /// annotator's answer, majority vote, or any other heuristic; adjudication
    /// is a human judgement call by design -- see
    /// docs/evidence-annotation-protocol.md#annotation-decisions). Raw
    /// per-annotator inputs are never mutated or discarded by this call --
    /// callers are expected to preserve them as their own separate files
    /// (see validation/annotations/README.md).
    /// </summary>
    public static GoldClaimAnnotationSet Adjudicate(
        string taskId,
        IReadOnlyList<GoldClaimAnnotation> resolvedClaims,
        string adjudicatorId = "adjudicated")
    {
        if (resolvedClaims.Count == 0)
            throw new ArgumentException("adjudication must resolve at least one claim");

        return new GoldClaimAnnotationSet(
            GoldClaimAnnotationReader.CurrentSchemaVersion,
            taskId,
            adjudicatorId,
            "adjudicated",
            resolvedClaims);
    }
}

/// <summary>Raw claim-by-claim comparison of two independent annotators. AgreementRate is descriptive only -- see AmlAgent.Evidence.AgreementStatistics for the chance-corrected statistic.</summary>
public sealed record ClaimSetComparison(
    int ComparedClaimCount,
    int RequiredMatchCount,
    IReadOnlyList<ClaimComparison> PerClaim,
    IReadOnlyList<string> ClaimIdsOnlyInFirst,
    IReadOnlyList<string> ClaimIdsOnlyInSecond)
{
    public double? AgreementRate => ComparedClaimCount == 0 ? null : (double)RequiredMatchCount / ComparedClaimCount;
}

public sealed record ClaimComparison(
    string ClaimId,
    bool RequiredMatches,
    IReadOnlyList<string> RequiredOnlyInFirst,
    IReadOnlyList<string> RequiredOnlyInSecond,
    int AcceptableAlternativesCountFirst,
    int AcceptableAlternativesCountSecond);
