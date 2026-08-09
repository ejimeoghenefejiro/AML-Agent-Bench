using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmlAgent.Adapters.Manifest;

/// <summary>
/// One research-validation test outcome: a single row of validation_result.json's
/// "results" array. task/case/model/judgeModel/datasetHash are all optional since
/// not every validation test (e.g. a pure EGHR-math gold check) runs against a
/// specific task, agent model, or dataset -- absence is recorded as null, never
/// guessed or omitted.
/// </summary>
public sealed record ValidationResultEntry(
    string TestCategory,
    string ExpectedResult,
    string ObservedResult,
    bool Passed,
    string? Task = null,
    string? Case = null,
    string? Model = null,
    string? JudgeModel = null,
    string? DatasetHash = null,
    string? Notes = null);

/// <summary>
/// Builds and writes validation_result.json: the machine-readable research
/// evidence artefact for AML-Agent-Bench's validation layer (research-validation
/// instructions, item 15). Provenance-first, matching AssuranceProfileBuilder's
/// convention for the benchmark's own output artefacts -- a git commit SHA, an
/// explicit validation-suite version, and a generation timestamp accompany every
/// batch of results, and limitations are recorded as a field, not an afterthought.
/// </summary>
public static class ValidationResultWriter
{
    public static JsonObject Build(
        IReadOnlyList<ValidationResultEntry> results,
        string validationSuiteVersion,
        string benchmarkVersion,
        string? repoRootForGitSha,
        string? limitations,
        DateTimeOffset? generatedAtUtc = null)
    {
        return new JsonObject
        {
            ["validation_suite_version"] = validationSuiteVersion,
            ["benchmark_version"] = benchmarkVersion,
            ["git_commit_sha"] = repoRootForGitSha is null ? null : GetGitCommitSha(repoRootForGitSha),
            ["generated_at_utc"] = (generatedAtUtc ?? DateTimeOffset.UtcNow).ToString("o"),
            ["limitations"] = limitations,
            ["result_count"] = results.Count,
            ["pass_count"] = results.Count(r => r.Passed),
            ["fail_count"] = results.Count(r => !r.Passed),
            ["results"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject
            {
                ["test_category"] = r.TestCategory,
                ["task"] = r.Task,
                ["case"] = r.Case,
                ["model"] = r.Model,
                ["judge_model"] = r.JudgeModel,
                ["dataset_hash"] = r.DatasetHash,
                ["expected_result"] = r.ExpectedResult,
                ["observed_result"] = r.ObservedResult,
                ["status"] = r.Passed ? "PASS" : "FAIL",
                ["notes"] = r.Notes,
            }).ToArray()),
        };
    }

    public static string Write(JsonObject validationResult, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, validationResult.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return outputPath;
    }

    private static string? GetGitCommitSha(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            return p.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null; // git not on PATH, not a repo, etc. -- field just stays null
        }
    }
}
