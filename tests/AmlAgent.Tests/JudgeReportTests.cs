using System.Text.Json;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Validates the shape of <c>judge_report.json</c> when the LLM-as-judge has
/// run against the workspace. Skipped if the file is absent (e.g. judge stage
/// was disabled, or this task has no rubric).
/// </summary>
public class JudgeReportTests
{
    private static string? ReportPath()
    {
        var ws = Environment.GetEnvironmentVariable("AML_BENCH_WORKSPACE");
        if (string.IsNullOrEmpty(ws)) return null;
        var p = Path.Combine(ws, "judge_report.json");
        return File.Exists(p) ? p : null;
    }

    private static JsonElement Report()
    {
        var p = ReportPath()!;
        using var doc = JsonDocument.Parse(File.ReadAllText(p));
        return doc.RootElement.Clone();
    }

    [SkippableFact]
    public void JudgeReportIsValidJson()
    {
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        Assert.True(root.ValueKind == JsonValueKind.Object);
    }

    [SkippableFact]
    public void JudgeReportHasRequiredFields()
    {
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        foreach (var field in new[] { "scores", "overall_score", "overall_max", "overall_percentage", "verdict" })
            Assert.True(root.TryGetProperty(field, out _), $"missing field: {field}");
    }

    [SkippableFact]
    public void JudgeReportHasNonEmptySchemaVersion()
    {
        // Fix #12: judge_report.json's shape has grown across several fixes
        // without ever being versioned -- baselined now so a future
        // claim-level or traceability-block change has an explicit signal.
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        Assert.True(root.TryGetProperty("schema_version", out var version), "missing field: schema_version");
        Assert.False(string.IsNullOrWhiteSpace(version.GetString()));
    }

    [SkippableFact]
    public void JudgeOverallPercentageMatchesScores()
    {
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        int sum = 0, max = 0;
        foreach (var dim in root.GetProperty("scores").EnumerateObject())
        {
            sum += dim.Value.GetProperty("score").GetInt32();
            max += dim.Value.GetProperty("max").GetInt32();
        }
        var reportedSum = root.GetProperty("overall_score").GetInt32();
        var reportedMax = root.GetProperty("overall_max").GetInt32();
        Assert.Equal(sum, reportedSum);
        Assert.Equal(max, reportedMax);
        var pct = root.GetProperty("overall_percentage").GetDouble();
        Assert.InRange(pct, 0.0, 1.0);
    }

    [SkippableFact]
    public void JudgeVerdictIsPass()
    {
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        var verdict = root.GetProperty("verdict").GetString();
        Assert.Equal("PASS", verdict);
    }

    [SkippableFact]
    public void RubricByCategoryFieldsAreInternallyConsistent()
    {
        // Fix #5: whichever categories the task's rubric.json dimensions
        // carry, each category's score/max/percentage must be internally
        // consistent, and every category subtotal must be <= the full
        // rubric total (a category is a subset of the whole, never more).
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        Skip.If(!root.TryGetProperty("rubric_by_category", out var byCategory), "no rubric_by_category (rubric predates fix #5)");

        var overallScore = root.GetProperty("overall_score").GetInt32();
        var overallMax = root.GetProperty("overall_max").GetInt32();

        foreach (var category in byCategory.EnumerateObject())
        {
            var score = category.Value.GetProperty("score").GetInt32();
            var max = category.Value.GetProperty("max").GetInt32();
            Assert.True(score <= max, $"category '{category.Name}': score ({score}) exceeds max ({max})");
            Assert.True(max <= overallMax, $"category '{category.Name}': max ({max}) exceeds overall_max ({overallMax})");
            Assert.True(score <= overallScore, $"category '{category.Name}': score ({score}) exceeds overall_score ({overallScore})");

            if (max > 0)
                Assert.Equal(Math.Round((double)score / max, 4), category.Value.GetProperty("percentage").GetDouble());
        }
    }

    [SkippableFact]
    public void OutcomeCorrectness_WhenPresent_MatchesItsOwnCategoryInRubricByCategory()
    {
        // The convenience top-level "outcome_correctness" field must always
        // agree with rubric_by_category["outcome_correctness"] -- it's a
        // derived alias, not an independent computation that could drift.
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        Skip.If(!root.TryGetProperty("outcome_correctness", out var outcome) || outcome.ValueKind == JsonValueKind.Null,
            "no outcome_correctness dimensions in this task's rubric");

        var byCategory = root.GetProperty("rubric_by_category").GetProperty("outcome_correctness");
        Assert.Equal(byCategory.GetProperty("score").GetInt32(), outcome.GetProperty("score").GetInt32());
        Assert.Equal(byCategory.GetProperty("max").GetInt32(), outcome.GetProperty("max").GetInt32());
    }

    [SkippableFact]
    public void MaterialClaims_WhenPresent_EveryEntryHasClaimIdAndAgentEvidenceArray()
    {
        // Fix #7: only meaningful for tasks whose evidence-annotations.json
        // defines material_claims (task-007 today) -- skipped otherwise, same
        // as every other conditional field in this file.
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        Skip.If(!root.TryGetProperty("material_claims", out var claims) || claims.ValueKind != JsonValueKind.Array || claims.GetArrayLength() == 0,
            "no material_claims in this task's judge_report.json");

        foreach (var claim in claims.EnumerateArray())
        {
            Assert.True(claim.TryGetProperty("claim_id", out var id) && id.GetString()!.Length > 0, "material claim missing claim_id");
            Assert.True(claim.TryGetProperty("agent_evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Array,
                $"claim '{id}' missing agent_evidence array");
            Assert.True(claim.TryGetProperty("material", out var material) && material.GetBoolean(),
                $"claim '{id}' should be material == true (task-authored material claims are material by construction)");
        }
    }

    [SkippableFact]
    public void EghrFieldsArePresentAndConsistent()
    {
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        Assert.True(root.TryGetProperty("eghr", out var eghr), "missing field: eghr");

        var total = eghr.GetProperty("total_claims").GetInt32();
        var supported = eghr.GetProperty("supported_count").GetInt32();
        var unsupported = eghr.GetProperty("unsupported_count").GetInt32();
        var contradicted = eghr.GetProperty("contradicted_count").GetInt32();
        var rate = eghr.GetProperty("rate").GetDouble();

        Assert.Equal(total, supported + unsupported + contradicted);
        Assert.InRange(rate, 0.0, 1.0);
        if (total > 0)
            Assert.Equal(Math.Round((double)(unsupported + contradicted) / total, 4), rate);
    }

    [SkippableFact]
    public void EvidenceTraceabilityFieldsArePresentAndConsistent()
    {
        var p = ReportPath();
        Skip.If(p is null, "no judge report");
        var root = Report();
        Assert.True(root.TryGetProperty("evidence_traceability", out var trace), "missing field: evidence_traceability");

        var grounded = trace.GetProperty("grounded_citations_distinct").GetInt32();
        var matched = trace.GetProperty("matched_gold_citations").GetInt32();
        Assert.True(matched <= grounded, "matched gold citations cannot exceed grounded citations");

        if (trace.GetProperty("precision").ValueKind == JsonValueKind.Number)
            Assert.InRange(trace.GetProperty("precision").GetDouble(), 0.0, 1.0);
        if (trace.GetProperty("recall").ValueKind == JsonValueKind.Number)
            Assert.InRange(trace.GetProperty("recall").GetDouble(), 0.0, 1.0);

        // Every fabricated citation must, by definition, be absent from the
        // grounding data — cross-check against the grounded/distinct counts.
        var fabricated = trace.GetProperty("fabricated_citations").GetArrayLength();
        var distinct = trace.GetProperty("cited_txn_ids_distinct").GetInt32();
        Assert.Equal(distinct, grounded + fabricated);
    }
}
