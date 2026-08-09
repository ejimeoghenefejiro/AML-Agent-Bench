using System.Text.Json.Nodes;
using AmlAgent.Adapters.Manifest;
using AmlAgent.Adapters.Normalisation;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Item 15: proves ValidationResultWriter produces a real, correctly-shaped
/// validation_result.json -- built from genuine outcomes of running actual
/// EGHR/traceability checks (not fabricated pass/fail values), and written to
/// disk under validation/outputs/, matching the suggested directory layout.
///
/// This demonstrates the writer works end to end; it does not claim every xUnit
/// test in this project automatically feeds into this artefact -- wiring a
/// custom xUnit result-collector to auto-populate this file for the FULL test
/// run is a natural follow-up, left explicitly for a later pass rather than
/// half-built here.
/// </summary>
public class ValidationResultArtifactTests
{
    private static readonly string RepoRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
    private static readonly string OutputPath = Path.Combine(RepoRoot, "validation", "outputs", "validation_result_sample.json");

    [Fact]
    public void Build_ProducesAllRequiredProvenanceFields()
    {
        var entries = new[]
        {
            new ValidationResultEntry("eghr_validation", "eghr_rate=0.0", "eghr_rate=0.0", Passed: true, Case: "zero_unsupported_claims"),
        };

        var artifact = ValidationResultWriter.Build(entries, "1.0.0", "AML-Agent-Bench 0.1 (PhD research prototype)", RepoRoot,
            "Deterministic layer only; LLM-judge stochastic behaviour covered separately by item 7.");

        Assert.Equal("1.0.0", (string?)artifact["validation_suite_version"]);
        Assert.NotNull(artifact["benchmark_version"]);
        Assert.NotNull(artifact["generated_at_utc"]);
        Assert.NotNull(artifact["limitations"]);
        Assert.Equal(1, (int)artifact["result_count"]!);
        Assert.Equal(1, (int)artifact["pass_count"]!);
        Assert.Equal(0, (int)artifact["fail_count"]!);
    }

    [Fact]
    public void Build_MissingGitRepo_LeavesShaNullRatherThanThrowing()
    {
        var entries = new[] { new ValidationResultEntry("x", "a", "a", true) };
        var artifact = ValidationResultWriter.Build(entries, "1.0.0", "bench", "C:\\does\\not\\exist", null);
        Assert.Null(artifact["git_commit_sha"]);
    }

    [Fact]
    public void Build_FailedEntry_RecordsExpectedVsObservedAndFailStatus()
    {
        var entries = new[] { new ValidationResultEntry("boundary", "throws AdapterNormalisationException", "no exception thrown", Passed: false, Notes: "regression") };
        var artifact = ValidationResultWriter.Build(entries, "1.0.0", "bench", null, null);

        var result = artifact["results"]!.AsArray()[0]!;
        Assert.Equal("FAIL", (string?)result["status"]);
        Assert.Equal("throws AdapterNormalisationException", (string?)result["expected_result"]);
        Assert.Equal("no exception thrown", (string?)result["observed_result"]);
        Assert.Equal(1, (int)artifact["fail_count"]!);
    }

    [Fact]
    public void EndToEnd_RealEghrAndTraceabilityChecks_ProduceARealValidationResultFile()
    {
        var entries = new List<ValidationResultEntry>();

        // Re-run genuine EGHR checks and record their real outcomes.
        var perfectClaims = new[] { new ClaimInput("a", new[] { "T1" }, "supported") };
        var perfectEghr = EvidenceScoring.ScoreClaims(perfectClaims, new HashSet<string> { "T1" });
        entries.Add(new ValidationResultEntry(
            "eghr_validation", "eghr_rate=0.0", $"eghr_rate={perfectEghr.Rate}",
            Passed: perfectEghr.Rate == 0.0, Case: "all_claims_supported"));

        var hallucinatedClaims = new[] { new ClaimInput("a", Array.Empty<string>(), "unsupported") };
        var hallucinatedEghr = EvidenceScoring.ScoreClaims(hallucinatedClaims, new HashSet<string>());
        entries.Add(new ValidationResultEntry(
            "eghr_validation", "eghr_rate=1.0", $"eghr_rate={hallucinatedEghr.Rate}",
            Passed: hallucinatedEghr.Rate == 1.0, Case: "all_claims_hallucinated"));

        // Re-run a genuine traceability check.
        var trace = EvidenceScoring.ComputeTraceability("T1-001 and T1-002", new HashSet<string> { "T1-001", "T1-002" }, new HashSet<string> { "T1-001", "T1-002" });
        entries.Add(new ValidationResultEntry(
            "traceability_validation", "precision=1.0,recall=1.0,f1=1.0", $"precision={trace.Precision},recall={trace.Recall},f1={trace.F1}",
            Passed: trace.Precision == 1.0 && trace.Recall == 1.0 && trace.F1 == 1.0, Case: "perfect_precision_and_recall"));

        // A real canonical hash, recorded as dataset_hash provenance.
        var dataset = AmlAgent.Adapters.Canonical.CanonicalAmlDataset.Empty();
        var datasetHash = CanonicalHashing.ComputeNormalisationHash(dataset);
        entries.Add(new ValidationResultEntry(
            "determinism", "same hash on repeat", "same hash on repeat",
            Passed: datasetHash == CanonicalHashing.ComputeNormalisationHash(dataset),
            DatasetHash: datasetHash, Case: "empty_dataset_hash_stable"));

        Assert.All(entries, e => Assert.True(e.Passed, $"{e.TestCategory}/{e.Case}: expected {e.ExpectedResult}, observed {e.ObservedResult}"));

        var artifact = ValidationResultWriter.Build(entries, "1.0.0", "AML-Agent-Bench 0.1 (PhD research prototype)", RepoRoot,
            "Deterministic layer only (EGHR/traceability math + canonical hashing); LLM-judge stochastic behaviour is item 7's separate concern.");

        ValidationResultWriter.Write(artifact, OutputPath);

        Assert.True(File.Exists(OutputPath));
        var reread = JsonNode.Parse(File.ReadAllText(OutputPath))!.AsObject();
        Assert.Equal(entries.Count, (int)reread["result_count"]!);
        Assert.Equal(entries.Count, (int)reread["pass_count"]!);
        Assert.Equal(0, (int)reread["fail_count"]!);
    }
}
