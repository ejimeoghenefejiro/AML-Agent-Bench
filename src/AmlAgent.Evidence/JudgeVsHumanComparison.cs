namespace AmlAgent.Evidence;

/// <summary>
/// Compares benchmark/judge output against human gold annotations. Every method
/// here returns RAW counts and confusion data -- never a single summary
/// statistic presented as "the" agreement score. Per the research-validation
/// instructions: "do not automatically claim validity from a single statistic".
/// A percentage agreement rate is included as a descriptive convenience on each
/// result record, not as a reliability/validity claim (it is not Cohen's Kappa
/// or any chance-corrected statistic -- deliberately not computed here, since
/// that would be exactly the kind of premature validity claim this item warns
/// against).
/// </summary>
public static class JudgeVsHumanComparison
{
    /// <summary>
    /// Compares the judge's per-claim support classification against one human
    /// annotator's classification of the same claims, by claim id. Claims present
    /// in only one side are reported separately, never silently dropped or
    /// treated as an implicit agreement/disagreement.
    /// </summary>
    public static ClassificationAgreement CompareClassifications(
        IReadOnlyDictionary<string, string> judgeClassificationsByClaimId,
        HumanAnnotator human)
    {
        var humanByClaimId = human.Claims.ToDictionary(c => c.ClaimId, c => c.Classification, StringComparer.OrdinalIgnoreCase);

        var comparedIds = judgeClassificationsByClaimId.Keys.Intersect(humanByClaimId.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyInJudge = judgeClassificationsByClaimId.Keys.Except(humanByClaimId.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var onlyInHuman = humanByClaimId.Keys.Except(judgeClassificationsByClaimId.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var confusion = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        int agree = 0, disagree = 0;

        foreach (var claimId in comparedIds)
        {
            var judgeLabel = judgeClassificationsByClaimId[claimId].Trim().ToLowerInvariant();
            var humanLabel = humanByClaimId[claimId].Trim().ToLowerInvariant();

            if (!confusion.TryGetValue(judgeLabel, out var row))
                confusion[judgeLabel] = row = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            row[humanLabel] = row.GetValueOrDefault(humanLabel) + 1;

            if (string.Equals(judgeLabel, humanLabel, StringComparison.OrdinalIgnoreCase)) agree++;
            else disagree++;
        }

        return new ClassificationAgreement(
            comparedIds.Count, agree, disagree,
            confusion.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value),
            onlyInJudge, onlyInHuman);
    }

    /// <summary>Compares the judge's per-claim cited evidence ids against one human annotator's, by claim id.</summary>
    public static EvidenceLinkAgreement CompareEvidenceLinks(
        IReadOnlyDictionary<string, IReadOnlyList<string>> judgeEvidenceIdsByClaimId,
        HumanAnnotator human)
    {
        var humanByClaimId = human.Claims.ToDictionary(c => c.ClaimId, c => c.EvidenceIds, StringComparer.OrdinalIgnoreCase);
        var comparedIds = judgeEvidenceIdsByClaimId.Keys.Intersect(humanByClaimId.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        var perClaim = new List<EvidenceLinkClaimComparison>();
        int totalJudge = 0, totalHuman = 0, totalOverlap = 0;

        foreach (var claimId in comparedIds)
        {
            var judgeIds = new HashSet<string>(judgeEvidenceIdsByClaimId[claimId], StringComparer.OrdinalIgnoreCase);
            var humanIds = new HashSet<string>(humanByClaimId[claimId], StringComparer.OrdinalIgnoreCase);

            var overlap = judgeIds.Intersect(humanIds, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var judgeOnly = judgeIds.Except(humanIds, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var humanOnly = humanIds.Except(judgeIds, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();

            perClaim.Add(new EvidenceLinkClaimComparison(claimId, judgeOnly, humanOnly, overlap));
            totalJudge += judgeIds.Count;
            totalHuman += humanIds.Count;
            totalOverlap += overlap.Count;
        }

        return new EvidenceLinkAgreement(comparedIds.Count, totalJudge, totalHuman, totalOverlap, perClaim);
    }

    /// <summary>Compares two human annotators' classifications of the same claims -- inter-annotator agreement, same mechanism as judge-vs-human.</summary>
    public static ClassificationAgreement CompareAnnotators(HumanAnnotator a, HumanAnnotator b)
    {
        var aByClaimId = a.Claims.ToDictionary(c => c.ClaimId, c => c.Classification, StringComparer.OrdinalIgnoreCase);
        return CompareClassifications(aByClaimId, b);
    }

    /// <summary>Compares the judge's rubric dimension scores against a human annotator's, where both provided them. Raw per-dimension differences, no aggregate validity claim.</summary>
    public static RubricScoreAgreement CompareRubricScores(IReadOnlyDictionary<string, double> judgeScores, HumanAnnotator human)
    {
        if (human.RubricScores is null)
            return new RubricScoreAgreement(0, Array.Empty<RubricDimensionComparison>(), null);

        var comparedDims = judgeScores.Keys.Intersect(human.RubricScores.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        var perDim = comparedDims.Select(d => new RubricDimensionComparison(d, judgeScores[d], human.RubricScores[d], judgeScores[d] - human.RubricScores[d])).ToList();
        double? meanAbsoluteDifference = perDim.Count == 0 ? null : perDim.Average(d => Math.Abs(d.Difference));

        return new RubricScoreAgreement(comparedDims.Count, perDim, meanAbsoluteDifference);
    }
}

/// <summary>Raw confusion data for a classification comparison. AgreementRate is descriptive only -- not a chance-corrected reliability statistic.</summary>
public sealed record ClassificationAgreement(
    int ComparedClaimCount,
    int AgreeCount,
    int DisagreeCount,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ConfusionMatrix,
    IReadOnlyList<string> ClaimIdsOnlyInFirst,
    IReadOnlyList<string> ClaimIdsOnlyInSecond)
{
    public double? AgreementRate => ComparedClaimCount == 0 ? null : (double)AgreeCount / ComparedClaimCount;
}

public sealed record EvidenceLinkAgreement(
    int ComparedClaimCount,
    int TotalJudgeEvidenceIds,
    int TotalHumanEvidenceIds,
    int TotalOverlap,
    IReadOnlyList<EvidenceLinkClaimComparison> PerClaim);

public sealed record EvidenceLinkClaimComparison(string ClaimId, IReadOnlyList<string> JudgeOnly, IReadOnlyList<string> HumanOnly, IReadOnlyList<string> Overlap);

public sealed record RubricScoreAgreement(int ComparedDimensionCount, IReadOnlyList<RubricDimensionComparison> PerDimension, double? MeanAbsoluteDifference);

public sealed record RubricDimensionComparison(string Dimension, double JudgeScore, double HumanScore, double Difference);
