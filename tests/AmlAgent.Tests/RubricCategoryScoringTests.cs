using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.RubricCategoryScoring (fix #5) -- the
/// pure logic behind separating outcome-correctness from citation-quality
/// rubric dimensions, so H4 can correlate task performance against evidence
/// traceability without one contaminating the other.
/// </summary>
public class RubricCategoryScoringTests
{
    private static readonly Dictionary<string, string?> Task007Categories = new()
    {
        ["network_identification"] = "outcome_correctness",
        ["evidence_grounding"] = "evidence_quality",
        ["avoids_unsupported_claims"] = "evidence_quality",
        ["evidence_traceability"] = "evidence_quality",
        ["avoids_false_implication"] = "outcome_correctness",
        ["typology_identification"] = "outcome_correctness",
        ["explanation_quality"] = "process_quality",
        ["audit_trail_awareness"] = "process_quality",
    };

    [Fact]
    public void ComputeCategoryTotals_SeparatesOutcomeCorrectnessFromEvidenceQuality()
    {
        // A report that reconstructs the network perfectly but cites nothing
        // (or fabricates everything) should score high outcome_correctness
        // and low evidence_quality -- the two must not be entangled.
        var scored = new[]
        {
            new RubricCategoryScoring.ScoredDimension("network_identification", 5, 5),
            new RubricCategoryScoring.ScoredDimension("avoids_false_implication", 5, 5),
            new RubricCategoryScoring.ScoredDimension("typology_identification", 5, 5),
            new RubricCategoryScoring.ScoredDimension("evidence_grounding", 0, 5),
            new RubricCategoryScoring.ScoredDimension("avoids_unsupported_claims", 0, 5),
            new RubricCategoryScoring.ScoredDimension("evidence_traceability", 0, 5),
            new RubricCategoryScoring.ScoredDimension("explanation_quality", 3, 5),
            new RubricCategoryScoring.ScoredDimension("audit_trail_awareness", 3, 5),
        };

        var totals = RubricCategoryScoring.ComputeCategoryTotals(scored, Task007Categories);

        Assert.Equal(1.0, totals["outcome_correctness"].Percentage);
        Assert.Equal(0.0, totals["evidence_quality"].Percentage);
        Assert.Equal(0.6, totals["process_quality"].Percentage);
    }

    [Fact]
    public void ComputeCategoryTotals_HighEvidenceQualityDoesNotInflateOutcomeCorrectness()
    {
        // The inverse: perfect citation hygiene but a wrong network
        // reconstruction must not make outcome_correctness look good.
        var scored = new[]
        {
            new RubricCategoryScoring.ScoredDimension("network_identification", 0, 5),
            new RubricCategoryScoring.ScoredDimension("avoids_false_implication", 0, 5),
            new RubricCategoryScoring.ScoredDimension("typology_identification", 0, 5),
            new RubricCategoryScoring.ScoredDimension("evidence_grounding", 5, 5),
            new RubricCategoryScoring.ScoredDimension("avoids_unsupported_claims", 5, 5),
            new RubricCategoryScoring.ScoredDimension("evidence_traceability", 5, 5),
        };

        var totals = RubricCategoryScoring.ComputeCategoryTotals(scored, Task007Categories);

        Assert.Equal(0.0, totals["outcome_correctness"].Percentage);
        Assert.Equal(1.0, totals["evidence_quality"].Percentage);
    }

    [Fact]
    public void ComputeCategoryTotals_UncategorisedDimension_IsExcludedFromEveryCategory()
    {
        // A rubric written before fix #5 (or a dimension deliberately left
        // uncategorised) must not silently fold into some default bucket --
        // it should simply not appear in any category total.
        var categories = new Dictionary<string, string?> { ["scored_dim"] = null };
        var scored = new[] { new RubricCategoryScoring.ScoredDimension("scored_dim", 3, 5) };

        var totals = RubricCategoryScoring.ComputeCategoryTotals(scored, categories);

        Assert.Empty(totals);
    }

    [Fact]
    public void ComputeCategoryTotals_DimensionMissingFromCategoryMap_IsExcluded()
    {
        var scored = new[] { new RubricCategoryScoring.ScoredDimension("not_in_map", 3, 5) };
        var totals = RubricCategoryScoring.ComputeCategoryTotals(scored, new Dictionary<string, string?>());
        Assert.Empty(totals);
    }

    [Fact]
    public void ComputeCategoryTotals_NoScoredDimensions_ReturnsEmptyDictionary()
    {
        var totals = RubricCategoryScoring.ComputeCategoryTotals(
            Array.Empty<RubricCategoryScoring.ScoredDimension>(), Task007Categories);
        Assert.Empty(totals);
    }

    [Fact]
    public void CategoryTotal_Percentage_IsNullNotZero_WhenMaxIsZero()
    {
        var total = new RubricCategoryScoring.CategoryTotal(Score: 0, Max: 0);
        Assert.Null(total.Percentage);
    }

    [Fact]
    public void ComputeCategoryTotals_MultipleDimensionsPerCategory_SumsCorrectly()
    {
        var scored = new[]
        {
            new RubricCategoryScoring.ScoredDimension("network_identification", 4, 5),
            new RubricCategoryScoring.ScoredDimension("avoids_false_implication", 3, 5),
            new RubricCategoryScoring.ScoredDimension("typology_identification", 5, 5),
        };

        var totals = RubricCategoryScoring.ComputeCategoryTotals(scored, Task007Categories);

        Assert.Equal(12, totals["outcome_correctness"].Score);
        Assert.Equal(15, totals["outcome_correctness"].Max);
        Assert.Equal(0.8, totals["outcome_correctness"].Percentage);
    }
}
