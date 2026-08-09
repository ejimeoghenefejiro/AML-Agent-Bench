using System.Text.Json.Nodes;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Item 1 of the research-validation instructions: proves the benchmark's
/// deterministic scoring machinery (EGHR + evidence traceability) discriminates
/// between known-good and deliberately faulty agent outputs -- "better grounded
/// output -> better score, worse/fabricated output -> worse score" -- using the
/// REAL gold evidence sets from task-006 and task-007.
///
/// Scoped to the deterministic layer only (no live LLM judge call): EGHR and
/// evidence-traceability are computed directly from hand-authored claims/report
/// text, exactly as items 2/3 do. Two of the eight requested categories
/// (incorrect_conclusion_plausible_explanation, and for task-007 also
/// over/under-reporting entities) cannot be discriminated by EGHR/traceability
/// alone -- this is demonstrated explicitly, not hidden, per each fixture's
/// "flag" field. aml-transaction-network (the third existing task) has no
/// rubric.json/gold-evidence-annotations at all, so it is not feasible to run
/// this experiment against it; task-006 and task-007 are the two tasks where
/// this is feasible.
/// </summary>
public class DiscriminationValidationTests
{
    private static readonly string RootDir = Path.Combine(AppContext.BaseDirectory, "validation", "gold", "discrimination");

    private sealed record Fixture(string Category, int QualityRank, EghrResult Eghr, TraceabilityResult Trace, JsonObject Raw);

    private static Fixture LoadFixture(string path)
    {
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var category = (string)json["category"]!;
        var rank = (int)json["quality_rank"]!;

        var claims = json["claims"]?.AsArray().Select(c => new ClaimInput(
            Text: (string)c!["text"]!,
            CitedTxnIds: c["cited_txn_ids"]!.AsArray().Select(id => (string)id!).ToList(),
            Support: (string)c["judge_support"]!)).ToList() ?? new List<ClaimInput>();

        // valid/gold txn ids are the same fixed sets for every fixture within a task
        // directory -- read from a shared sibling file to avoid repeating them in
        // every fixture and risking a copy/paste drift between them.
        var taskDir = Path.GetDirectoryName(path)!;
        var universe = JsonNode.Parse(File.ReadAllText(Path.Combine(taskDir, "_universe.json")))!.AsObject();
        var validTxnIds = new HashSet<string>(universe["valid_txn_ids"]!.AsArray().Select(n => (string)n!), StringComparer.OrdinalIgnoreCase);
        var goldTxnIds = new HashSet<string>(universe["gold_txn_ids"]!.AsArray().Select(n => (string)n!), StringComparer.OrdinalIgnoreCase);

        var eghr = EvidenceScoring.ScoreClaims(claims, validTxnIds);
        var reportText = (string?)json["report_text"] ?? "";
        var trace = EvidenceScoring.ComputeTraceability(reportText, validTxnIds, goldTxnIds);

        return new Fixture(category, rank, eghr, trace, json);
    }

    private static Dictionary<string, Fixture> LoadTask(string taskName) =>
        Directory.GetFiles(Path.Combine(RootDir, taskName), "0*.json")
            .Select(LoadFixture)
            .ToDictionary(f => f.Category);

    private static double Composite(Fixture f) => (1.0 - f.Eghr.Rate) * (f.Trace.F1 ?? 0.0);

    public static IEnumerable<object[]> Tasks() => new[] { new object[] { "task-006" }, new object[] { "task-007" } };

    [Theory]
    [MemberData(nameof(Tasks))]
    public void PerfectReport_DiscreteCountsMatchGoldExpectation(string task)
    {
        var fixtures = LoadTask(task);
        foreach (var (category, f) in fixtures)
        {
            var expected = f.Raw["expected_discrete"]?.AsObject();
            if (expected is null || !expected.ContainsKey("matched_gold_citations")) continue; // entity-classification-only fixtures (07/08) have no citation counts

            Assert.True((int)expected["matched_gold_citations"]! == f.Trace.MatchedGoldCitations, $"[{task}/{category}] matched_gold_citations");
            Assert.True((int)expected["missing_gold_citations_count"]! == f.Trace.MissingGoldCitationsList.Count, $"[{task}/{category}] missing_gold_citations_count");
            var expectedUnsupported = (int)expected["unsupported_or_contradicted_claim_count"]!;
            Assert.True(expectedUnsupported == f.Eghr.UnsupportedCount + f.Eghr.ContradictedCount, $"[{task}/{category}] unsupported_or_contradicted_claim_count");
        }
    }

    [Theory]
    [MemberData(nameof(Tasks))]
    public void PerfectReport_ScoresBestPossibleOnBothAxes(string task)
    {
        var f1 = LoadTask(task)["correct_answer_correct_evidence"];
        Assert.Equal(0.0, f1.Eghr.Rate);
        Assert.Equal(1.0, f1.Trace.Precision);
        Assert.Equal(1.0, f1.Trace.Recall);
        Assert.Equal(1.0, f1.Trace.F1);
    }

    [Theory]
    [MemberData(nameof(Tasks))]
    public void PerfectReport_CompositeScoreIsAtLeastAsHighAsEveryOtherCategory(string task)
    {
        // Provable by construction: EGHR>=0 means (1-EGHR)<=1, and traceability F1<=1
        // always, so composite<=1==perfect-report's composite for every other fixture,
        // including the two "invisible to these metrics" categories which tie rather
        // than lose -- that tie is itself the documented finding, not a test bug.
        var fixtures = LoadTask(task);
        var perfect = Composite(fixtures["correct_answer_correct_evidence"]);
        foreach (var (category, f) in fixtures)
        {
            if (f.Raw["claims"] is null) continue; // entity-classification-only fixtures don't participate in this axis
            Assert.True(perfect >= Composite(f), $"[{task}] perfect report's composite ({perfect}) should be >= {category}'s ({Composite(f)})");
        }
    }

    [Theory]
    [MemberData(nameof(Tasks))]
    public void IncompleteEvidence_ScoresStrictlyWorseRecallThanPerfectReport(string task)
    {
        var fixtures = LoadTask(task);
        var perfect = fixtures["correct_answer_correct_evidence"];
        var incomplete = fixtures["correct_conclusion_incomplete_evidence"];

        Assert.True(incomplete.Trace.Recall < perfect.Trace.Recall);
        Assert.Equal(0.0, incomplete.Eghr.Rate); // nothing fabricated or hallucinated, just thin
    }

    [Theory]
    [MemberData(nameof(Tasks))]
    public void MissingImportantEvidence_ScoresStrictlyWorseRecallThanMerelyIncomplete(string task)
    {
        var fixtures = LoadTask(task);
        var incomplete = fixtures["correct_conclusion_incomplete_evidence"];
        var missing = fixtures["missing_important_gold_evidence"];

        Assert.True(missing.Trace.Recall < incomplete.Trace.Recall,
            $"[{task}] severely-thin coverage ({missing.Trace.Recall}) should score worse recall than merely-incomplete coverage ({incomplete.Trace.Recall})");
    }

    [Theory]
    [MemberData(nameof(Tasks))]
    public void FabricatedCitation_DegradesEghrButNotTraceability_ConfirmingTheFlaggedFinding(string task)
    {
        var fixtures = LoadTask(task);
        var perfect = fixtures["correct_answer_correct_evidence"];
        var fabricated = fixtures["correct_conclusion_fabricated_citation"];

        Assert.True(fabricated.Eghr.Rate > perfect.Eghr.Rate, $"[{task}] fabrication must degrade EGHR");
        Assert.Equal(perfect.Trace.Precision, fabricated.Trace.Precision);
        Assert.Equal(perfect.Trace.Recall, fabricated.Trace.Recall);
        Assert.Equal(perfect.Trace.F1, fabricated.Trace.F1);
    }

    [Theory]
    [MemberData(nameof(Tasks))]
    public void IncorrectConclusion_IsCompletelyInvisibleToDeterministicMetrics_ConfirmingTheFlaggedGap(string task)
    {
        var fixtures = LoadTask(task);
        var perfect = fixtures["correct_answer_correct_evidence"];
        var wrongConclusion = fixtures["incorrect_conclusion_plausible_explanation"];

        // This is the documented scope limitation, asserted as a positive fact
        // rather than left as an implicit gap: a wrong conclusion built on
        // real citations scores IDENTICALLY to the correct one.
        Assert.Equal(perfect.Eghr.Rate, wrongConclusion.Eghr.Rate);
        Assert.Equal(perfect.Trace.Precision, wrongConclusion.Trace.Precision);
        Assert.Equal(perfect.Trace.Recall, wrongConclusion.Trace.Recall);
        Assert.Equal(perfect.Trace.F1, wrongConclusion.Trace.F1);
    }

    [Theory]
    [MemberData(nameof(Tasks))]
    public void Hallucination_DegradesBothEghrAndRecallSimultaneously(string task)
    {
        var fixtures = LoadTask(task);
        var perfect = fixtures["correct_answer_correct_evidence"];
        var hallucinated = fixtures["unsupported_claim_hallucination"];

        Assert.True(hallucinated.Eghr.Rate > perfect.Eghr.Rate);
        Assert.True(hallucinated.Trace.Recall < perfect.Trace.Recall);
        // Strictly dominated on both axes -> composite must be strictly lower, not tied.
        Assert.True(Composite(hallucinated) < Composite(perfect));
    }

    // -- Entity-classification axis (task-007 only): over/under-reporting are not
    // citation-grounding failures at all, so they are measured against
    // mule_network_findings.csv-shaped classification rows, not EGHR/traceability. --

    private static readonly HashSet<string> InnocentAccounts = new(StringComparer.OrdinalIgnoreCase) { "N150", "N160", "N170" };
    private static readonly HashSet<string> SuspiciousAccounts = new(StringComparer.OrdinalIgnoreCase) { "N100", "M201", "M202", "M301", "EXT401" };
    private static readonly HashSet<string> ImplicatingClassifications = new(StringComparer.OrdinalIgnoreCase) { "mule", "exit_point", "watchlist_match" };

    private static (List<string> FalsePositives, List<string> FalseNegatives) ClassifyFindings(JsonArray findings)
    {
        var byId = findings.ToDictionary(f => (string)f!["account_id"]!, f => (string)f!["classification"]!, StringComparer.OrdinalIgnoreCase);

        var falsePositives = InnocentAccounts.Where(id => byId.TryGetValue(id, out var c) && ImplicatingClassifications.Contains(c)).ToList();
        var falseNegatives = SuspiciousAccounts.Where(id => id != "N100" // the victim isn't "suspicious" in the mule/exit sense
            && (!byId.TryGetValue(id, out var c) || !ImplicatingClassifications.Contains(c))).ToList();

        return (falsePositives, falseNegatives);
    }

    [Fact]
    public void PerfectFindings_HasNoFalsePositivesOrFalseNegatives()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(RootDir, "task-007", "01_correct_answer_correct_evidence.json")))!.AsObject();
        var (fp, fn) = ClassifyFindings(fixture["findings"]!.AsArray());
        Assert.Empty(fp);
        Assert.Empty(fn);
    }

    [Fact]
    public void OverReporting_FlagsInnocentAccountsAsFalsePositives()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(RootDir, "task-007", "07_over_reporting_innocent_entities.json")))!.AsObject();
        var (fp, fn) = ClassifyFindings(fixture["findings"]!.AsArray());

        Assert.Equal(2, fp.Count);
        Assert.Contains("N150", fp);
        Assert.Contains("N160", fp);
        Assert.Empty(fn);
    }

    [Fact]
    public void UnderReporting_MissesSuspiciousAccountAsFalseNegative()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(RootDir, "task-007", "08_under_reporting_suspicious_entities.json")))!.AsObject();
        var (fp, fn) = ClassifyFindings(fixture["findings"]!.AsArray());

        Assert.Empty(fp);
        Assert.Single(fn);
        Assert.Contains("M301", fn);
    }

    [Fact]
    public void OverReportingAndUnderReporting_AreDistinguishableFromEachOtherAndFromPerfect()
    {
        // The whole discrimination claim for this axis, in one assertion:
        // three genuinely different failure signatures, not collapsed together.
        var perfectFixture = JsonNode.Parse(File.ReadAllText(Path.Combine(RootDir, "task-007", "01_correct_answer_correct_evidence.json")))!.AsObject();
        var overFixture = JsonNode.Parse(File.ReadAllText(Path.Combine(RootDir, "task-007", "07_over_reporting_innocent_entities.json")))!.AsObject();
        var underFixture = JsonNode.Parse(File.ReadAllText(Path.Combine(RootDir, "task-007", "08_under_reporting_suspicious_entities.json")))!.AsObject();

        var (perfectFp, perfectFn) = ClassifyFindings(perfectFixture["findings"]!.AsArray());
        var (overFp, overFn) = ClassifyFindings(overFixture["findings"]!.AsArray());
        var (underFp, underFn) = ClassifyFindings(underFixture["findings"]!.AsArray());

        Assert.True(perfectFp.Count == 0 && perfectFn.Count == 0);
        Assert.True(overFp.Count > 0 && overFn.Count == 0);
        Assert.True(underFp.Count == 0 && underFn.Count > 0);
    }

    [Fact]
    public void AllEightRequiredCategoriesArePresent_Task007()
    {
        var categories = Directory.GetFiles(Path.Combine(RootDir, "task-007"), "0*.json")
            .Select(f => (string)JsonNode.Parse(File.ReadAllText(f))!["category"]!)
            .ToHashSet();

        var required = new[]
        {
            "correct_answer_correct_evidence", "correct_conclusion_incomplete_evidence",
            "correct_conclusion_fabricated_citation", "incorrect_conclusion_plausible_explanation",
            "unsupported_claim_hallucination", "missing_important_gold_evidence",
            "over_reporting_innocent_entities", "under_reporting_suspicious_entities",
        };
        foreach (var r in required) Assert.Contains(r, categories);
    }

    [Fact]
    public void SixApplicableCategoriesArePresent_Task006()
    {
        // over/under-reporting entities do not apply to task-006's output shape
        // (a temporal CSV summary + narrative report, no entity classification) --
        // documented as not-applicable rather than silently absent.
        var categories = Directory.GetFiles(Path.Combine(RootDir, "task-006"), "0*.json")
            .Select(f => (string)JsonNode.Parse(File.ReadAllText(f))!["category"]!)
            .ToHashSet();

        var required = new[]
        {
            "correct_answer_correct_evidence", "correct_conclusion_incomplete_evidence",
            "correct_conclusion_fabricated_citation", "incorrect_conclusion_plausible_explanation",
            "unsupported_claim_hallucination", "missing_important_gold_evidence",
        };
        foreach (var r in required) Assert.Contains(r, categories);
        Assert.Equal(6, categories.Count);
    }
}
