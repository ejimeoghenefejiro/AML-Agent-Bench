using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Items 8+9: exercises HumanAnnotationReader and JudgeVsHumanComparison against
/// a SYNTHETIC test fixture (validation/gold/human-annotations/example_synthetic_annotation.json)
/// -- no real human annotations exist yet, per the instructions' "do not fabricate
/// human annotations". This proves the schema/loader/comparison tooling actually
/// works, so real annotations can be dropped in later with zero code changes.
/// </summary>
public class HumanAnnotationTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "validation", "gold", "human-annotations", "example_synthetic_annotation.json");

    [Fact]
    public void Parse_SyntheticFixture_ReadsCaseAndOutputIds()
    {
        var set = HumanAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        Assert.Equal("task-007-case-001", set.CaseId);
        Assert.Equal("agent-output-003", set.OutputId);
    }

    [Fact]
    public void Parse_SyntheticFixture_ReadsBothAnnotatorsWithTheirClaims()
    {
        var set = HumanAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        Assert.Equal(2, set.Annotators.Count);

        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");
        Assert.Equal(3, h01.Claims.Count);
        var c1 = h01.Claims.Single(c => c.ClaimId == "C1");
        Assert.Equal("supported", c1.Classification);
        Assert.Equal(new[] { "T1-001", "T1-002" }, c1.EvidenceIds);
    }

    [Fact]
    public void Parse_SyntheticFixture_ReadsOptionalRubricScores()
    {
        var set = HumanAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");
        Assert.NotNull(h01.RubricScores);
        Assert.Equal(4, h01.RubricScores!["network_identification"]);
    }

    [Fact]
    public void Parse_MissingCaseId_ThrowsInvalidHumanAnnotationException()
    {
        const string json = """{ "output_id": "x", "annotators": [{"annotator_id":"H01","claims":[]}] }""";
        var ex = Assert.Throws<InvalidHumanAnnotationException>(() => HumanAnnotationReader.Parse(json));
        Assert.Contains("case_id", ex.Message);
    }

    [Fact]
    public void Parse_MissingAnnotators_ThrowsInvalidHumanAnnotationException()
    {
        const string json = """{ "case_id": "x", "output_id": "y" }""";
        Assert.Throws<InvalidHumanAnnotationException>(() => HumanAnnotationReader.Parse(json));
    }

    [Fact]
    public void Parse_ClaimMissingClassification_ThrowsInvalidHumanAnnotationException()
    {
        const string json = """
        { "case_id": "x", "output_id": "y", "annotators": [
          { "annotator_id": "H01", "claims": [ { "claim_id": "C1", "evidence_ids": [] } ] }
        ]}
        """;
        var ex = Assert.Throws<InvalidHumanAnnotationException>(() => HumanAnnotationReader.Parse(json));
        Assert.Contains("classification", ex.Message);
    }

    // -- Judge-vs-human comparison (item 9), all against the synthetic fixture --

    [Fact]
    public void CompareClassifications_JudgeVsH01_RawConfusionCountsAreCorrect()
    {
        var set = HumanAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");

        // Synthetic judge output for testing: agrees on C1/C3, disagrees on C2.
        var judge = new Dictionary<string, string> { ["C1"] = "supported", ["C2"] = "supported", ["C3"] = "contradicted" };

        var result = JudgeVsHumanComparison.CompareClassifications(judge, h01);

        Assert.Equal(3, result.ComparedClaimCount);
        Assert.Equal(2, result.AgreeCount); // C1, C3
        Assert.Equal(1, result.DisagreeCount); // C2: judge=supported, human=unsupported
        Assert.Equal(1, result.ConfusionMatrix["supported"]["unsupported"]);
        Assert.Equal(0.6667, result.AgreementRate!.Value, precision: 4);
    }

    [Fact]
    public void CompareClassifications_ClaimsOnlyInOneSide_AreReportedNotSilentlyDropped()
    {
        var set = HumanAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");

        var judge = new Dictionary<string, string> { ["C1"] = "supported", ["C99"] = "supported" }; // C99 doesn't exist in human annotation

        var result = JudgeVsHumanComparison.CompareClassifications(judge, h01);

        Assert.Contains("C99", result.ClaimIdsOnlyInFirst);
        Assert.Contains("C2", result.ClaimIdsOnlyInSecond);
        Assert.Contains("C3", result.ClaimIdsOnlyInSecond);
        Assert.Equal(1, result.ComparedClaimCount); // only C1 is comparable
    }

    [Fact]
    public void CompareEvidenceLinks_JudgeVsH01_RawOverlapCountsAreCorrect()
    {
        var set = HumanAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");

        var judge = new Dictionary<string, IReadOnlyList<string>>
        {
            ["C1"] = new[] { "T1-001", "T1-099" }, // T1-099 is judge-only (e.g. a fabricated citation)
        };

        var result = JudgeVsHumanComparison.CompareEvidenceLinks(judge, h01);

        var c1 = result.PerClaim.Single(c => c.ClaimId == "C1");
        Assert.Contains("T1-001", c1.Overlap);
        Assert.Contains("T1-099", c1.JudgeOnly);
        Assert.Contains("T1-002", c1.HumanOnly);
    }

    [Fact]
    public void CompareAnnotators_H01VsH02_InterAnnotatorDisagreementOnC2IsCaptured()
    {
        var set = HumanAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");
        var h02 = set.Annotators.Single(a => a.AnnotatorId == "H02");

        var result = JudgeVsHumanComparison.CompareAnnotators(h01, h02);

        Assert.Equal(3, result.ComparedClaimCount);
        Assert.Equal(2, result.AgreeCount); // C1, C3
        Assert.Equal(1, result.DisagreeCount); // C2: H01=unsupported, H02=supported
    }

    [Fact]
    public void CompareRubricScores_JudgeVsH01_RawPerDimensionDifferencesAreCorrect()
    {
        var set = HumanAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");

        var judgeScores = new Dictionary<string, double> { ["network_identification"] = 5, ["evidence_grounding"] = 5, ["avoids_false_implication"] = 5 };

        var result = JudgeVsHumanComparison.CompareRubricScores(judgeScores, h01);

        Assert.Equal(3, result.ComparedDimensionCount);
        var netId = result.PerDimension.Single(d => d.Dimension == "network_identification");
        Assert.Equal(5, netId.JudgeScore);
        Assert.Equal(4, netId.HumanScore);
        Assert.Equal(1, netId.Difference);
        Assert.NotNull(result.MeanAbsoluteDifference);
    }

    [Fact]
    public void CompareRubricScores_AnnotatorWithNoRubricScores_ReturnsEmptyNotAnError()
    {
        var annotatorWithoutRubric = new HumanAnnotator("H03", new[] { new HumanClaimAnnotation("C1", "supported", Array.Empty<string>()) });
        var result = JudgeVsHumanComparison.CompareRubricScores(new Dictionary<string, double> { ["x"] = 5 }, annotatorWithoutRubric);

        Assert.Equal(0, result.ComparedDimensionCount);
        Assert.Null(result.MeanAbsoluteDifference);
    }
}
