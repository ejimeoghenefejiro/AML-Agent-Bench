namespace AmlAgent.Evidence;

/// <summary>
/// Aggregates a judge's per-dimension rubric scores into per-category
/// subtotals (fix #5). A task's rubric.json dimensions may each carry an
/// optional "category" -- typically "outcome_correctness", "evidence_quality",
/// or "process_quality" -- so a construct-clean outcome-correctness score
/// (network reconstruction, typology, innocent-account clearing -- no
/// citation-quality terms) can be reported separately from the full rubric
/// score, which still legitimately mixes citation quality in as a holistic
/// "is this report good enough to ship" gate. Without this separation, H4
/// (task performance vs. evidence traceability) would correlate a variable
/// against itself, since the old "task performance" was the full rubric
/// including evidence_grounding/avoids_unsupported_claims/evidence_traceability
/// dimensions. See docs/evidence-traceability-framework.md
/// #outcome-correctness-vs-task-performance.
///
/// Pure and dependency-free, like the rest of AmlAgent.Evidence -- no JSON
/// parsing, no I/O. Callers (JudgeAgent.cs) own translating rubric.json's
/// dimension list and the judge's scores object into the inputs here.
/// </summary>
public static class RubricCategoryScoring
{
    /// <summary>One dimension's score, as scored by the judge for one run.</summary>
    public sealed record ScoredDimension(string DimensionId, int Score, int Max);

    /// <summary>A category's aggregated score. Percentage is null (not zero) when Max is 0 -- "not measured", not "measured as zero".</summary>
    public sealed record CategoryTotal(int Score, int Max)
    {
        public double? Percentage => Max == 0 ? null : Math.Round((double)Score / Max, 4);
    }

    /// <summary>
    /// Sums Score/Max per category. A dimension with no category (null, or
    /// absent from dimensionCategories -- e.g. every rubric written before
    /// this fix) is excluded from every category total, not silently folded
    /// into one -- categorisation is opt-in per dimension, so an
    /// uncategorised rubric produces an empty result rather than a
    /// misleading total.
    /// </summary>
    public static IReadOnlyDictionary<string, CategoryTotal> ComputeCategoryTotals(
        IEnumerable<ScoredDimension> scoredDimensions,
        IReadOnlyDictionary<string, string?> dimensionCategories)
    {
        var totals = new Dictionary<string, (int Score, int Max)>();
        foreach (var dim in scoredDimensions)
        {
            if (!dimensionCategories.TryGetValue(dim.DimensionId, out var category) || category is null)
                continue;

            var (s, m) = totals.TryGetValue(category, out var existing) ? existing : (0, 0);
            totals[category] = (s + dim.Score, m + dim.Max);
        }
        return totals.ToDictionary(kv => kv.Key, kv => new CategoryTotal(kv.Value.Score, kv.Value.Max));
    }
}
