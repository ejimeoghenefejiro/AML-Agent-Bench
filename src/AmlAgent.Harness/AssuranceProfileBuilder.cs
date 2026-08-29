using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmlAgent.Evidence;

namespace AmlAgent.Harness;

/// <summary>
/// Builds assurance_profile.json from an already-assembled bench_result.json
/// (see ReportBuilder), a selected assurance policy (default:
/// assurance/policy.default.json; override with --policy), and
/// AmlAgent.Evidence.AssuranceEngine's decision logic. This is the first
/// concrete step of the "Real-World AI Assurance Profile" direction (see
/// Proposal/AML Agent Bench Real World Assurance Profile.txt and its
/// CLI-Only Assurance Roadmap follow-up, and assurance/README.md): a
/// machine-readable, evidence-backed answer to "is this agent suitable for
/// operational deployment, and under what conditions?" -- scoped honestly
/// to what this benchmark actually measures.
///
/// Only produced when a judge ran (the metrics it evaluates -- EGHR,
/// evidence traceability, fabricated citations -- all come from the judge
/// report). Returns null when there's nothing to assess, e.g. --oracle runs.
/// </summary>
internal static class AssuranceProfileBuilder
{
    private const string BenchmarkVersion = "AML-Agent-Bench 0.1 (PhD research prototype)";

    public static JsonObject? Build(JsonObject benchResult, string workspace, string repoRoot, string? policyPathOverride)
    {
        var judge = benchResult["judge"]?.AsObject();
        if (judge is null || judge["scores"] is null)
            return null; // nothing to assess -- no judge ran for this task/run

        var policyPath = policyPathOverride is not null
            ? (Path.IsPathRooted(policyPathOverride) ? policyPathOverride : Path.Combine(repoRoot, policyPathOverride))
            : Path.Combine(repoRoot, "assurance", "policy.default.json");
        if (!File.Exists(policyPath))
            throw new FileNotFoundException($"assurance policy not found: {policyPath}");

        var policy = JsonNode.Parse(File.ReadAllText(policyPath))?.AsObject()
            ?? throw new InvalidDataException($"{policyPath} is not valid JSON");

        var thresholds = (policy["thresholds"]?.AsArray() ?? new JsonArray())
            .Select(t => new MetricThreshold(
                Metric: (string)t!["metric"]!,
                Label: (string)t["label"]!,
                Direction: (string)t["direction"]!,
                Threshold: (double)t["threshold"]!,
                Unit: (string)t["unit"]!,
                Required: (bool?)t["required"] ?? true))
            .ToList();

        // Fail loudly on a malformed/impossible policy rather than silently
        // producing a nonsensical decision from it.
        AssuranceEngine.ValidatePolicy(thresholds);

        var values = GatherMetricValues(judge);
        var results = thresholds
            .Select(t => AssuranceEngine.EvaluateMetric(t, values.GetValueOrDefault(t.Metric)))
            .ToList();

        var notImplemented = (policy["not_yet_implemented_dimensions"]?.AsArray() ?? new JsonArray())
            .Select(n => (string)n!).ToList();

        var decision = AssuranceEngine.Decide(results, notImplemented);

        // Case-level evidence integrity is a distinct gate from the judge's
        // agent-level metrics above: EGHR/fabricated-citations/missing-gold-evidence
        // all ask "did the agent's report stay grounded in the evidence it was
        // given". case_manifest.json (written by StageCanonicalCaseIfPresent for
        // tasks with a case-definition.json) instead asks "was the evidence itself
        // trustworthy" -- dangling/incompatible references and cross-source
        // duplicate-content conflicts in the canonical case, independent of
        // anything the agent said. A benchmark run is not assurance-valid if this
        // fails, no matter how well the agent's report scored.
        var (caseIntegrity, caseIntegrityAssessment) = EvaluateCaseEvidenceIntegrity(workspace);
        decision = AssuranceEngine.ApplyCaseIntegrityGate(decision, caseIntegrityAssessment);

        var task = (string?)benchResult["task"] ?? "unknown-task";
        var executionStatus = (int?)benchResult["agent_exit_code"] == 0 ? "completed" : "failed";
        var benchmarkVerdict = (string?)benchResult["overall_verdict"] ?? "-";

        var rubricPath = Path.Combine(repoRoot, "tasks", task, "rubric.json");

        var profile = new JsonObject
        {
            ["schema_version"] = "0.2",
            ["disclaimer"] = "PhD research prototype, not a certification. See assurance/README.md for exactly what is and is not measured. A generated PASS_WITH_CONDITIONS or PASS reflects only the metrics this benchmark actually evaluates, listed under 'metrics' -- it is not a claim of regulatory approval or compliance.",
            ["generated_at_utc"] = DateTime.UtcNow.ToString("o"),

            // Separated per the CLI-Only Assurance Roadmap: a benchmark PASS
            // (xUnit + judge) must never be read as a deployment PASS. These
            // three can and do disagree -- that disagreement is the point.
            ["status_summary"] = new JsonObject
            {
                ["execution_status"] = executionStatus,
                ["benchmark_verdict"] = benchmarkVerdict,
                ["assurance_decision"] = decision.Overall,
            },

            ["agent"] = benchResult["agent"]?.DeepClone(),
            ["benchmark"] = BenchmarkVersion,
            ["scenario_pack"] = task,
            ["operational_capabilities"] = LoadOperationalCapabilities(repoRoot, task),
            ["jurisdiction_profile"] = "generic (jurisdiction-specific regulatory profiles not implemented -- see assurance/README.md)",
            ["policy"] = new JsonObject
            {
                ["id"] = (string?)policy["policy_id"],
                ["name"] = (string?)policy["policy_name"],
                ["version"] = (string?)policy["policy_version"],
                ["path"] = Path.GetRelativePath(repoRoot, policyPath).Replace('\\', '/'),
                ["is_illustrative_example"] = true,
            },
            ["metrics"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject
            {
                ["metric"] = r.Metric,
                ["label"] = r.Label,
                ["value"] = r.Value,
                ["unit"] = r.Unit,
                ["threshold"] = r.Threshold?.Threshold,
                ["direction"] = r.Threshold?.Direction,
                ["required"] = r.Threshold?.Required ?? true,
                ["status"] = r.Status,
            }).ToArray()),
            ["not_evaluated_dimensions"] = new JsonArray(decision.NotEvaluatedDimensions.Select(d => (JsonNode)d).ToArray()),
            ["deployment_decision"] = new JsonObject
            {
                ["overall"] = decision.Overall,
                ["reason"] = decision.Reason,
                ["evaluated_metric_count"] = decision.EvaluatedCount,
                ["total_defined_dimension_count"] = decision.TotalDefinedCount,
                ["reasons"] = new JsonArray(decision.Reasons.Select(r => (JsonNode)new JsonObject
                {
                    ["metric"] = r.Metric,
                    ["label"] = r.Label,
                    ["actual"] = r.Actual,
                    ["threshold"] = r.Threshold,
                    ["rule"] = r.Rule,
                    ["severity"] = r.Severity,
                }).ToArray()),
            },
            ["deployment_restrictions"] = policy["deployment_restrictions_if_pass_with_conditions"]?.DeepClone(),
            ["evidence_summary"] = new JsonObject
            {
                ["eghr"] = judge["eghr"]?.DeepClone(), // legacy/secondary metric: unsupported/contradicted claims (see docs/evidence-traceability-framework.md#legacy-eghr-metric)
                ["evidence_traceability"] = judge["evidence_traceability"]?.DeepClone(), // includes fabricated_citations and missing_gold_citations_list
                ["claims"] = judge["claims"]?.DeepClone(),
            },
            // Additive, backward-compatible re-organisation of the same measurements
            // above around the traceability failure taxonomy (see
            // AmlAgent.Evidence.EvidenceTraceabilityProfileBuilder and
            // docs/evidence-traceability-framework.md). Fields not yet
            // computable (evidence_sufficiency_rate) are explicit nulls, never
            // fabricated. claim_support_coverage/claim_level_precision/recall/f1
            // (fix #7) are computed when judge_report.json carries a
            // "material_claims" array (JudgeAgent.cs writes one when the task's
            // evidence-annotations.json defines material claims -- task-007
            // today); ClaimJson.ParseArray round-trips it back into typed
            // Claim objects, staying null for every task that hasn't opted in.
            ["evidence_traceability_profile"] = EvidenceTraceabilityProfileBuilder.Build(
                judge["eghr"]?.AsObject(), judge["evidence_traceability"]?.AsObject(),
                ParseMaterialClaimsOrNull(judge["material_claims"]?.AsArray())),
            ["case_evidence_integrity"] = caseIntegrity,
            ["provenance"] = new JsonObject
            {
                ["run_id"] = benchResult["run_id"]?.DeepClone(),
                ["workspace"] = workspace,
                ["started_at_utc"] = benchResult["started_at_utc"]?.DeepClone(),
                ["completed_at_utc"] = benchResult["completed_at_utc"]?.DeepClone(),
                ["execution_mode"] = benchResult["agent"]?["mode"]?.DeepClone(),
                ["benchmark_version"] = BenchmarkVersion,
                ["git_commit_sha"] = GetGitCommitSha(repoRoot),
                ["policy_id"] = (string?)policy["policy_id"],
                ["policy_version"] = (string?)policy["policy_version"],
                ["policy_file_hash"] = ComputeFileHash(policyPath),
                ["dataset_hash"] = ComputeDatasetHash(repoRoot, task),
                ["rubric_hash"] = ComputeFileHash(rubricPath),
                ["task_fingerprint"] = ComputeTaskFingerprint(repoRoot, task),
                ["benchmark_config_hash"] = ComputeBenchmarkConfigHash(task, benchResult, policy),

                // Agent/model identity as configured. "agent_version" is
                // honestly "unversioned" -- there is no version scheme for
                // in-repo agents yet, recorded rather than omitted so the
                // gap is visible.
                ["agent_version"] = "unversioned",
                ["model_identifier"] = benchResult["agent"]?["model"]?.DeepClone(),
                ["temperature"] = 0.0, // hardcoded in BenchmarkAgent.cs / JudgeAgent.cs's OpenAIPromptExecutionSettings

                // OpenAI's "seed" parameter (where available) is documented
                // by the provider as best-effort, not a determinism
                // guarantee -- it is deliberately NOT wired up or claimed
                // here. Recording null (not omitting the field) makes the
                // gap explicit rather than silently absent.
                ["random_seed"] = null,

                ["judge_model"] = judge["model"]?.DeepClone(),
                ["judge_config"] = new JsonObject
                {
                    ["pass_threshold_overall"] = judge["pass_threshold_overall"]?.DeepClone(),
                    ["rubric_path"] = Path.GetRelativePath(repoRoot, rubricPath).Replace('\\', '/'),
                    ["temperature"] = 0.0,
                },

                ["runtime"] = new JsonObject
                {
                    ["dotnet_version"] = Environment.Version.ToString(),
                    ["os"] = Environment.OSVersion.ToString(),
                },

                ["reproducibility_note"] = "Deterministic where the pipeline controls it (evidence scoring, traceability, policy evaluation, hashes) -- see AmlAgent.Evidence.EvidenceScoring/AssuranceEngine unit tests. NOT claimed deterministic for the underlying LLM's own output: OpenAI does not guarantee reproducible completions even at temperature 0 without a provider-guaranteed seed, which is not configured here (see random_seed).",
            },
        };

        profile["result_hash"] = ComputeHash(profile);
        AssuranceProfileSchema.ValidateOrThrow(profile);
        return profile;
    }

    /// <summary>
    /// Reads tasks/&lt;task&gt;/capabilities.json's operational_capabilities
    /// array, if the task declares one. Absence is not an error -- it just
    /// means this task hasn't been tagged yet, which the empty array (not a
    /// fabricated guess) makes visible.
    /// </summary>
    private static JsonArray LoadOperationalCapabilities(string repoRoot, string task)
    {
        var path = Path.Combine(repoRoot, "tasks", task, "capabilities.json");
        if (!File.Exists(path)) return new JsonArray();

        var doc = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        var caps = doc?["operational_capabilities"]?.AsArray();
        if (caps is null) return new JsonArray();

        var values = caps.Select(c => (string?)c).Where(s => s is not null).Select(s => (JsonNode)s!);
        return new JsonArray(values.ToArray());
    }

    /// <summary>
    /// Reads workspace/case_manifest.json (written by
    /// Program.StageCanonicalCaseIfPresent when a task has a
    /// case-definition.json), if present, and hands its counts to
    /// AssuranceEngine.EvaluateCaseIntegrity for the actual decision logic --
    /// this method is pure I/O + JSON shaping, no gating rules of its own.
    /// Absent case_manifest.json (a task with no multi-source case) is not an
    /// error -- "present": false and no reasons, so existing single-source
    /// tasks see no behaviour change at all.
    /// </summary>
    private static (JsonObject Profile, CaseIntegrityAssessment Assessment) EvaluateCaseEvidenceIntegrity(string workspace)
    {
        var manifestPath = Path.Combine(workspace, "case_manifest.json");
        var manifest = File.Exists(manifestPath) ? JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() : null;
        var integrity = manifest?["evidence_integrity"]?.AsObject();
        if (integrity is null)
            return (new JsonObject { ["present"] = false }, AssuranceEngine.EvaluateCaseIntegrity(false, 0, 0));

        var dangling = integrity["dangling_references"]?.AsArray() ?? new JsonArray();
        var missingTxn = integrity["missing_transaction_references"]?.AsArray() ?? new JsonArray();
        var incompatible = integrity["incompatible_evidence_types"]?.AsArray() ?? new JsonArray();
        var duplicates = integrity["duplicate_evidence_ids"]?.AsArray() ?? new JsonArray();
        var invalidRefCount = dangling.Count + missingTxn.Count + incompatible.Count;
        var brokenLineageCount = duplicates.Count;

        var assessment = AssuranceEngine.EvaluateCaseIntegrity(true, invalidRefCount, brokenLineageCount);

        var profile = new JsonObject
        {
            ["present"] = true,
            ["status"] = (string?)integrity["status"],
            ["case_manifest_path"] = "case_manifest.json",
            ["canonical_case_hash"] = (string?)manifest?["canonical_case_hash"],
            ["invalid_source_evidence_reference"] = new JsonObject
            {
                ["count"] = invalidRefCount,
                ["dangling_references"] = dangling.DeepClone(),
                ["missing_transaction_references"] = missingTxn.DeepClone(),
                ["incompatible_evidence_types"] = incompatible.DeepClone(),
            },
            ["broken_canonical_evidence_lineage"] = new JsonObject
            {
                ["count"] = brokenLineageCount,
                ["duplicate_evidence_ids"] = duplicates.DeepClone(),
            },
        };

        return (profile, assessment);
    }

    /// <summary>
    /// Reconstructs typed Claim objects from judge_report.json's "material_claims"
    /// array (see ClaimJson, JudgeAgent.cs fix #7). Returns null -- not an empty
    /// list -- when the field is absent, so EvidenceTraceabilityProfileBuilder.Build
    /// leaves claim_scores/claim_support_coverage/etc. as null ("not measured")
    /// rather than an empty array ("measured, zero material claims") for every
    /// task that hasn't opted into claim-level annotation.
    /// </summary>
    private static IReadOnlyList<Claim>? ParseMaterialClaimsOrNull(JsonArray? array)
    {
        if (array is null) return null;
        var claims = ClaimJson.ParseArray(array);
        return claims.Count == 0 ? null : claims;
    }

    /// <summary>Pulls the raw metric values this benchmark actually computes out of a judge_report.json-shaped object.</summary>
    private static Dictionary<string, double?> GatherMetricValues(JsonObject judge)
    {
        var eghr = judge["eghr"]?.AsObject();
        var trace = judge["evidence_traceability"]?.AsObject();

        return new Dictionary<string, double?>
        {
            ["eghr_rate"] = (double?)eghr?["rate"],
            ["evidence_traceability_f1"] = (double?)trace?["f1"],
            ["fabricated_citation_count"] = trace?["fabricated_citations"]?.AsArray()?.Count,
            // Full rubric score (all dimensions, including citation-quality
            // ones) -- a holistic "is this report good enough" gate. NOT the
            // right variable for correlating against evidence traceability
            // (see outcome_correctness_percentage below) -- fix #5.
            ["task_performance_percentage"] = (double?)judge["overall_percentage"],
            // Construct-clean outcome-correctness score (fix #5): rubric
            // dimensions tagged "outcome_correctness" only -- network
            // reconstruction, typology, innocent-account clearing -- with no
            // citation-quality terms. This is the variable H4 should
            // correlate against evidence_traceability_f1/precision/recall;
            // task_performance_percentage above contains evidence_traceability
            // itself as one of its dimensions and would contaminate that
            // comparison. Null when the task's rubric.json has no
            // outcome_correctness-tagged dimensions (rubrics predating this
            // fix), not 0 -- "not measured", not "measured as zero".
            ["outcome_correctness_percentage"] = (double?)judge["outcome_correctness"]?["percentage"],
        };
    }

    public static void Write(JsonObject profile, string workspace, string repoRoot, string task, string agentName, DateTime startedUtc)
    {
        var serialised = profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var inWorkspace = Path.Combine(workspace, "assurance_profile.json");
        File.WriteAllText(inWorkspace, serialised);
        Console.WriteLine($"[harness] wrote {inWorkspace}");

        try
        {
            var dir = Path.Combine(repoRoot, "assurance-profiles");
            Directory.CreateDirectory(dir);
            var stamp = startedUtc.ToString("yyyyMMdd-HHmmss");
            var safeName = $"{stamp}-{task}-{agentName}.json";
            var archived = Path.Combine(dir, safeName);
            File.WriteAllText(archived, serialised);
            Console.WriteLine($"[harness] archived  {archived}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harness] could not write assurance-profiles/ archival copy: {ex.Message}");
        }
    }

    private static string ComputeHash(JsonObject profileWithoutHash)
    {
        var canonical = profileWithoutHash.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? ComputeFileHash(string path)
    {
        if (!File.Exists(path)) return null;
        var bytes = SHA256.HashData(File.ReadAllBytes(path));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Hashes every file under tasks/&lt;task&gt;/environment/data/ (sorted, concatenated) so a profile records exactly which dataset produced it.</summary>
    private static string? ComputeDatasetHash(string repoRoot, string task)
    {
        var dataDir = Path.Combine(repoRoot, "tasks", task, "environment", "data");
        if (!Directory.Exists(dataDir)) return null;
        var files = Directory.GetFiles(dataDir).OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0) return null;

        using var sha = SHA256.Create();
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(f);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return "sha256:" + Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// A stand-in for "task version": there is no explicit version field on
    /// tasks yet, so this hashes everything that defines the task's actual
    /// content (prompt/instruction text, rubric, gold-evidence annotations)
    /// -- if any of those change, this fingerprint changes, which is the
    /// practically useful property even without a human-assigned version
    /// number.
    /// </summary>
    private static string? ComputeTaskFingerprint(string repoRoot, string task)
    {
        var taskDir = Path.Combine(repoRoot, "tasks", task);
        if (!Directory.Exists(taskDir)) return null;

        var candidates = new[] { "prompt.md", "instruction.md", "rubric.json", "evidence-annotations.json", "capabilities.json" };
        var files = candidates
            .Select(name => Path.Combine(taskDir, name))
            .Where(File.Exists)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0) return null;

        using var sha = SHA256.Create();
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(f);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return "sha256:" + Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// Hashes the run's own configuration (task, model, max steps, mode,
    /// policy identity) into one fingerprint, so "was this run configured
    /// the same way as that one" is a single string comparison.
    /// </summary>
    private static string ComputeBenchmarkConfigHash(string task, JsonObject benchResult, JsonObject policy)
    {
        var agent = benchResult["agent"]?.AsObject();
        var canonical = string.Join('|', new[]
        {
            task,
            (string?)agent?["model"] ?? "",
            (string?)agent?["max_steps"]?.ToString() ?? "",
            (string?)agent?["mode"] ?? "",
            (string?)policy["policy_id"] ?? "",
            (string?)policy["policy_version"] ?? "",
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
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
            return null; // git not on PATH, not a repo, etc. -- provenance field just stays null
        }
    }
}
