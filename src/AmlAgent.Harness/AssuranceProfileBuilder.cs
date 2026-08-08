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
                ["eghr"] = judge["eghr"]?.DeepClone(),
                ["evidence_traceability"] = judge["evidence_traceability"]?.DeepClone(),
                ["claims"] = judge["claims"]?.DeepClone(),
            },
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
                ["dataset_hash"] = ComputeDatasetHash(repoRoot, task),
                ["rubric_hash"] = ComputeFileHash(rubricPath),
            },
        };

        profile["result_hash"] = ComputeHash(profile);
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
            ["task_performance_percentage"] = (double?)judge["overall_percentage"],
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
