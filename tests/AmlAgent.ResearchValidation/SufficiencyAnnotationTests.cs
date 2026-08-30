using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Fix #8: exercises SufficiencyAnnotationReader against a SYNTHETIC test
/// fixture (validation/gold/sufficiency/example_synthetic_sufficiency_annotation.json)
/// -- no real human sufficiency annotations exist yet. This proves the
/// schema/loader actually works, so a real annotation round can be dropped
/// in later with zero code changes.
///
/// Deliberately does NOT test, call, or reference any evidence_sufficiency_rate
/// computation, because none exists: per the PhD's stated sequencing, Evidence
/// Sufficiency Rate stays unimplemented (an explicit null in
/// EvidenceTraceabilityProfileBuilder.Build) until this schema has been used
/// for a real, validated annotation round. This test file proves the schema
/// and loader are ready for that round, nothing more.
/// </summary>
public class SufficiencyAnnotationTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "validation", "gold", "sufficiency", "example_synthetic_sufficiency_annotation.json");

    [Fact]
    public void Parse_SyntheticFixture_ReadsCaseAndOutputIds()
    {
        var set = SufficiencyAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        Assert.Equal("task-007-case-001", set.CaseId);
        Assert.Equal("agent-output-003", set.OutputId);
    }

    [Fact]
    public void Parse_SyntheticFixture_ReadsBothAnnotatorsWithAllFiveJudgementsEach()
    {
        var set = SufficiencyAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        Assert.Equal(2, set.Annotators.Count);

        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");
        Assert.Equal(5, h01.Judgements.Count);
    }

    [Fact]
    public void Parse_SyntheticFixture_ReadsSufficientLabelWithMinimumSets()
    {
        var set = SufficiencyAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");

        var mc1 = h01.Judgements.Single(j => j.ClaimId == "MC1");
        Assert.Equal("sufficient", mc1.SufficiencyLabel);
        Assert.NotNull(mc1.MinimumSufficientEvidenceSets);
        Assert.Equal(2, mc1.MinimumSufficientEvidenceSets!.Count);
        Assert.Contains(mc1.MinimumSufficientEvidenceSets, set => set.SequenceEqual(new[] { "T1-001", "T1-002" }));
    }

    [Fact]
    public void Parse_SyntheticFixture_ReadsInsufficientLabelWithRationale()
    {
        var set = SufficiencyAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");

        var mc3 = h01.Judgements.Single(j => j.ClaimId == "MC3");
        Assert.Equal("insufficient", mc3.SufficiencyLabel);
        Assert.False(string.IsNullOrWhiteSpace(mc3.Rationale));
    }

    [Fact]
    public void Parse_SyntheticFixture_ReadsOverbroadLabelWithNullMinimumSets()
    {
        var set = SufficiencyAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01 = set.Annotators.Single(a => a.AnnotatorId == "H01");

        var mc5 = h01.Judgements.Single(j => j.ClaimId == "MC5");
        Assert.Equal("overbroad", mc5.SufficiencyLabel);
        Assert.Null(mc5.MinimumSufficientEvidenceSets);
    }

    [Fact]
    public void Parse_SyntheticFixture_CapturesGenuineAnnotatorDisagreementOnMc5()
    {
        // The fixture deliberately includes a real disagreement (H01: overbroad,
        // H02: insufficient) on MC5 -- proving this schema can represent
        // disagreement rather than only ever encoding a clean consensus, which
        // is the whole point of collecting more than one annotator.
        var set = SufficiencyAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        var h01Label = set.Annotators.Single(a => a.AnnotatorId == "H01").Judgements.Single(j => j.ClaimId == "MC5").SufficiencyLabel;
        var h02Label = set.Annotators.Single(a => a.AnnotatorId == "H02").Judgements.Single(j => j.ClaimId == "MC5").SufficiencyLabel;

        Assert.NotEqual(h01Label, h02Label);
    }

    [Fact]
    public void Parse_MissingSchemaVersion_ThrowsInvalidSufficiencyAnnotationException()
    {
        // Fix #12: schema_version is a required field on the annotation file
        // itself, since (unlike judge_report.json, which this codebase
        // generates) a sufficiency-annotation file is authored externally by
        // annotators and needs to declare what schema it was written against.
        const string json = """{ "case_id": "x", "output_id": "y", "annotators": [{"annotator_id":"H01","claim_sufficiency":[]}] }""";
        var ex = Assert.Throws<InvalidSufficiencyAnnotationException>(() => SufficiencyAnnotationReader.Parse(json));
        Assert.Contains("schema_version", ex.Message);
    }

    [Fact]
    public void Parse_SyntheticFixture_SchemaVersionMatchesReaderCurrentVersion()
    {
        var set = SufficiencyAnnotationReader.Parse(File.ReadAllText(FixturePath), FixturePath);
        Assert.Equal(SufficiencyAnnotationReader.CurrentSchemaVersion, set.SchemaVersion);
    }

    [Fact]
    public void Parse_MissingCaseId_ThrowsInvalidSufficiencyAnnotationException()
    {
        const string json = """{ "schema_version": "1.0", "output_id": "x", "annotators": [{"annotator_id":"H01","claim_sufficiency":[]}] }""";
        var ex = Assert.Throws<InvalidSufficiencyAnnotationException>(() => SufficiencyAnnotationReader.Parse(json));
        Assert.Contains("case_id", ex.Message);
    }

    [Fact]
    public void Parse_MissingOutputId_ThrowsInvalidSufficiencyAnnotationException()
    {
        const string json = """{ "schema_version": "1.0", "case_id": "x", "annotators": [{"annotator_id":"H01","claim_sufficiency":[]}] }""";
        var ex = Assert.Throws<InvalidSufficiencyAnnotationException>(() => SufficiencyAnnotationReader.Parse(json));
        Assert.Contains("output_id", ex.Message);
    }

    [Fact]
    public void Parse_MissingAnnotators_ThrowsInvalidSufficiencyAnnotationException()
    {
        const string json = """{ "schema_version": "1.0", "case_id": "x", "output_id": "y" }""";
        Assert.Throws<InvalidSufficiencyAnnotationException>(() => SufficiencyAnnotationReader.Parse(json));
    }

    [Fact]
    public void Parse_JudgementMissingSufficiencyLabel_ThrowsInvalidSufficiencyAnnotationException()
    {
        const string json = """
        { "schema_version": "1.0", "case_id": "x", "output_id": "y", "annotators": [
          { "annotator_id": "H01", "claim_sufficiency": [ { "claim_id": "MC1" } ] }
        ]}
        """;
        var ex = Assert.Throws<InvalidSufficiencyAnnotationException>(() => SufficiencyAnnotationReader.Parse(json));
        Assert.Contains("sufficiency_label", ex.Message);
    }

    [Fact]
    public void Parse_InvalidSufficiencyLabel_ThrowsInvalidSufficiencyAnnotationException()
    {
        const string json = """
        { "schema_version": "1.0", "case_id": "x", "output_id": "y", "annotators": [
          { "annotator_id": "H01", "claim_sufficiency": [ { "claim_id": "MC1", "sufficiency_label": "maybe" } ] }
        ]}
        """;
        var ex = Assert.Throws<InvalidSufficiencyAnnotationException>(() => SufficiencyAnnotationReader.Parse(json));
        Assert.Contains("maybe", ex.Message);
    }

    [Fact]
    public void Parse_ValidLabelsAreAllAccepted()
    {
        foreach (var label in new[] { "sufficient", "insufficient", "overbroad" })
        {
            var json = $$"""
            { "schema_version": "1.0", "case_id": "x", "output_id": "y", "annotators": [
              { "annotator_id": "H01", "claim_sufficiency": [ { "claim_id": "MC1", "sufficiency_label": "{{label}}" } ] }
            ]}
            """;
            var set = SufficiencyAnnotationReader.Parse(json);
            Assert.Equal(label, set.Annotators[0].Judgements[0].SufficiencyLabel);
        }
    }
}
