namespace AmlAgent.Evidence;

/// <summary>
/// Chance-corrected inter-rater agreement statistics (v0.3 validation-priorities
/// item 1). AmlAgent.Evidence.JudgeVsHumanComparison's own doc comment
/// previously stated "no premature validity statistic (e.g. Kappa) is
/// computed here, deliberately" -- this class is that statistic, built now
/// that a real multi-annotator annotation round is the actual next concrete
/// step for this PhD, not a hypothetical one. This does not itself make any
/// annotation round "validated" -- it is the arithmetic a real round needs,
/// nothing more. It must only ever be called on genuinely independent
/// ratings; see AgreementStatisticsTests.cs for the synthetic, clearly-labelled
/// fixtures used only to prove the arithmetic is correct, never presented as
/// a real reliability finding.
///
/// Two statistics, for the two rater-count regimes docs/evidence-annotation-protocol.md#multi-annotator-validation
/// already names: Cohen's kappa (exactly two raters) and Fleiss' kappa (three
/// or more raters, same fixed rater count per item). Krippendorff's alpha
/// (mixed/missing data, uneven rater counts per item) is NOT implemented here
/// -- a real next step once a pilot's actual data shape is known, not before.
/// </summary>
public static class AgreementStatistics
{
    /// <summary>
    /// Cohen's kappa (Cohen, 1960) for two raters labelling the same ordered
    /// set of items with one of a finite set of categories each:
    /// kappa = (p_o - p_e) / (1 - p_e), where p_o is observed proportion
    /// agreement and p_e is the agreement expected by chance given each
    /// rater's own marginal label distribution. 1.0 = perfect agreement,
    /// 0.0 = chance-level agreement, negative = worse than chance. Null when
    /// there are no items to compare, or when p_e is 1.0 (both raters used
    /// only a single, identical category throughout -- 1 - p_e would be a
    /// division by zero, and "how much better than chance" is undefined when
    /// there was no variation for chance to act on).
    /// </summary>
    public static double? ComputeCohensKappa(IReadOnlyList<string> rater1Labels, IReadOnlyList<string> rater2Labels)
    {
        if (rater1Labels.Count != rater2Labels.Count)
            throw new ArgumentException($"raters must label the same number of items (rater1={rater1Labels.Count}, rater2={rater2Labels.Count})");

        int n = rater1Labels.Count;
        if (n == 0) return null;

        var categories = rater1Labels.Concat(rater2Labels).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rater1Counts = categories.ToDictionary(c => c, _ => 0, StringComparer.OrdinalIgnoreCase);
        var rater2Counts = categories.ToDictionary(c => c, _ => 0, StringComparer.OrdinalIgnoreCase);

        int observed = 0;
        for (int i = 0; i < n; i++)
        {
            rater1Counts[rater1Labels[i]]++;
            rater2Counts[rater2Labels[i]]++;
            if (string.Equals(rater1Labels[i], rater2Labels[i], StringComparison.OrdinalIgnoreCase))
                observed++;
        }

        double pObserved = (double)observed / n;
        double pExpected = categories.Sum(c => ((double)rater1Counts[c] / n) * ((double)rater2Counts[c] / n));

        if (pExpected >= 1.0) return null;
        return Math.Round((pObserved - pExpected) / (1 - pExpected), 4);
    }

    /// <summary>
    /// Fleiss' kappa (Fleiss, 1971) for three or more raters, where every
    /// item is labelled by the same fixed NUMBER of raters (not necessarily
    /// the same specific individuals per item) into one of a finite set of
    /// categories. ratingsPerItem is one inner list of labels per item, from
    /// whichever raters rated it. Throws if any item has a different number
    /// of ratings than the first -- Fleiss' kappa assumes a fixed rater count;
    /// a genuinely uneven-coverage annotation round needs Krippendorff's
    /// alpha instead (not implemented here -- see this class's own summary).
    /// Same 1.0/0.0/negative interpretation as Cohen's kappa. Null when there
    /// are no items, or when the expected-by-chance agreement is 1.0 (every
    /// rating across every item was the same single category).
    /// </summary>
    public static double? ComputeFleissKappa(IReadOnlyList<IReadOnlyList<string>> ratingsPerItem)
    {
        if (ratingsPerItem.Count == 0) return null;

        int raterCount = ratingsPerItem[0].Count;
        if (raterCount < 2)
            throw new ArgumentException("Fleiss' kappa requires at least two ratings per item");
        for (int i = 0; i < ratingsPerItem.Count; i++)
        {
            if (ratingsPerItem[i].Count != raterCount)
                throw new ArgumentException($"Fleiss' kappa requires the same number of ratings for every item (item 0 has {raterCount}, item {i} has {ratingsPerItem[i].Count})");
        }

        int n = ratingsPerItem.Count;
        var categories = ratingsPerItem.SelectMany(r => r).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (categories.Count == 0) return null;

        var perItemCounts = ratingsPerItem.Select(item =>
        {
            var counts = categories.ToDictionary(c => c, _ => 0, StringComparer.OrdinalIgnoreCase);
            foreach (var label in item) counts[label]++;
            return counts;
        }).ToList();

        // P_i: this item's own extent of pairwise agreement among its raters.
        var perItemAgreement = perItemCounts
            .Select(counts => (counts.Values.Sum(v => (double)v * v) - raterCount) / (raterCount * (raterCount - 1.0)))
            .ToList();
        double meanAgreement = perItemAgreement.Average();

        // p_j: proportion of ALL ratings (across all items) assigned to category j.
        var categoryProportions = categories
            .Select(cat => perItemCounts.Sum(counts => counts[cat]) / (double)(n * raterCount))
            .ToList();
        double expectedAgreement = categoryProportions.Sum(p => p * p);

        if (expectedAgreement >= 1.0) return null;
        return Math.Round((meanAgreement - expectedAgreement) / (1 - expectedAgreement), 4);
    }
}
