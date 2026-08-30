using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.AgreementStatistics (v0.3 validation-
/// priorities item 1) -- Cohen's kappa and Fleiss' kappa arithmetic, checked
/// against hand-computed textbook-style examples. These are synthetic,
/// clearly-labelled fixtures used only to prove the arithmetic is correct;
/// they are never presented as a real reliability finding for this PhD's
/// annotation data, which does not exist yet.
/// </summary>
public class AgreementStatisticsTests
{
    // -- Cohen's kappa --

    [Fact]
    public void ComputeCohensKappa_ClassicTextbookExample_MatchesHandComputedValue()
    {
        // Standard 2x2 confusion-matrix example: 50 items, categories yes/no.
        //            Rater2:Yes  Rater2:No
        // Rater1:Yes     20          5
        // Rater1:No      10         15
        // p_o = 35/50 = 0.70; rater1 marginals 0.5/0.5; rater2 marginals 0.6/0.4;
        // p_e = 0.5*0.6 + 0.5*0.4 = 0.50; kappa = (0.70-0.50)/(1-0.50) = 0.40.
        var r1 = new List<string>();
        var r2 = new List<string>();
        void Add(string a, string b, int count) { for (int i = 0; i < count; i++) { r1.Add(a); r2.Add(b); } }
        Add("yes", "yes", 20);
        Add("yes", "no", 5);
        Add("no", "yes", 10);
        Add("no", "no", 15);

        var kappa = AgreementStatistics.ComputeCohensKappa(r1, r2);
        Assert.Equal(0.4, kappa!.Value, precision: 4);
    }

    [Fact]
    public void ComputeCohensKappa_PerfectAgreement_IsOne()
    {
        var labels = new[] { "a", "b", "a", "c", "b" };
        var kappa = AgreementStatistics.ComputeCohensKappa(labels, labels);
        Assert.Equal(1.0, kappa!.Value, precision: 4);
    }

    [Fact]
    public void ComputeCohensKappa_BothRatersAlwaysAgreeOnSingleCategory_IsNull()
    {
        // p_e = 1.0 here (both raters used only one category throughout) --
        // 1 - p_e is a division by zero, so this is undefined, not zero.
        var labels = new[] { "a", "a", "a" };
        Assert.Null(AgreementStatistics.ComputeCohensKappa(labels, labels));
    }

    [Fact]
    public void ComputeCohensKappa_NoItems_IsNull()
    {
        Assert.Null(AgreementStatistics.ComputeCohensKappa(Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void ComputeCohensKappa_MismatchedItemCounts_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AgreementStatistics.ComputeCohensKappa(new[] { "a", "b" }, new[] { "a" }));
    }

    [Fact]
    public void ComputeCohensKappa_CaseInsensitiveCategories()
    {
        var r1 = new[] { "Yes", "No" };
        var r2 = new[] { "yes", "no" };
        Assert.Equal(1.0, AgreementStatistics.ComputeCohensKappa(r1, r2)!.Value, precision: 4);
    }

    [Fact]
    public void ComputeCohensKappa_WorseThanChance_IsNegative()
    {
        // Two raters who systematically disagree score below zero, not zero
        // or null -- a real, meaningful (bad) result.
        var r1 = new[] { "a", "a", "b", "b" };
        var r2 = new[] { "b", "b", "a", "a" };
        var kappa = AgreementStatistics.ComputeCohensKappa(r1, r2);
        Assert.True(kappa < 0, $"expected negative kappa for systematic disagreement, got {kappa}");
    }

    // -- Fleiss' kappa --

    [Fact]
    public void ComputeFleissKappa_HandComputedThreeRaterExample_MatchesExpectedValue()
    {
        // 4 items, 3 raters, categories {A,B}:
        //   item1: A,A,A (unanimous)
        //   item2: B,B,B (unanimous)
        //   item3: A,A,B
        //   item4: A,B,B
        // p_A = p_B = 0.5 -> p_e = 0.5; mean P_i = (1 + 1 + 1/3 + 1/3)/4 = 0.6667
        // kappa = (0.6667 - 0.5) / (1 - 0.5) = 0.3333
        var ratings = new List<IReadOnlyList<string>>
        {
            new[] { "A", "A", "A" },
            new[] { "B", "B", "B" },
            new[] { "A", "A", "B" },
            new[] { "A", "B", "B" },
        };

        var kappa = AgreementStatistics.ComputeFleissKappa(ratings);
        Assert.Equal(0.3333, kappa!.Value, precision: 4);
    }

    [Fact]
    public void ComputeFleissKappa_PerfectAgreement_IsOne()
    {
        var ratings = new List<IReadOnlyList<string>>
        {
            new[] { "A", "A", "A" },
            new[] { "B", "B", "B" },
            new[] { "A", "A", "A" },
        };
        Assert.Equal(1.0, AgreementStatistics.ComputeFleissKappa(ratings)!.Value, precision: 4);
    }

    [Fact]
    public void ComputeFleissKappa_NoItems_IsNull()
    {
        Assert.Null(AgreementStatistics.ComputeFleissKappa(Array.Empty<IReadOnlyList<string>>()));
    }

    [Fact]
    public void ComputeFleissKappa_FewerThanTwoRatersPerItem_Throws()
    {
        var ratings = new List<IReadOnlyList<string>> { new[] { "A" } };
        Assert.Throws<ArgumentException>(() => AgreementStatistics.ComputeFleissKappa(ratings));
    }

    [Fact]
    public void ComputeFleissKappa_UnevenRaterCountAcrossItems_Throws()
    {
        var ratings = new List<IReadOnlyList<string>>
        {
            new[] { "A", "A", "A" },
            new[] { "A", "B" }, // only 2 raters here, item 0 has 3 -- Fleiss' kappa needs a fixed count
        };
        Assert.Throws<ArgumentException>(() => AgreementStatistics.ComputeFleissKappa(ratings));
    }
}
