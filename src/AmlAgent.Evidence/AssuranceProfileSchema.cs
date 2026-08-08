using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmlAgent.Evidence;

/// <summary>
/// Formal (hand-written, dependency-free) schema validation for a generated
/// assurance_profile.json. A profile that violates this schema must fail
/// generation explicitly -- this is the check the CLI-Only Assurance
/// Roadmap's item 5 calls for. No external JSON-Schema library dependency;
/// the shape is simple and stable enough that an explicit checker is both
/// sufficient and easier to unit test with clear failure messages.
/// </summary>
public static class AssuranceProfileSchema
{
    private static readonly string[] RequiredTopLevelFields =
    {
        "schema_version", "disclaimer", "generated_at_utc", "status_summary",
        "agent", "benchmark", "scenario_pack", "operational_capabilities",
        "jurisdiction_profile", "policy", "metrics", "not_evaluated_dimensions",
        "deployment_decision", "provenance", "result_hash",
    };

    private static readonly string[] RequiredStatusSummaryFields =
        { "execution_status", "benchmark_verdict", "assurance_decision" };

    private static readonly string[] RequiredMetricFields =
        { "metric", "label", "value", "unit", "threshold", "direction", "required", "status" };

    private static readonly string[] RequiredDecisionFields =
        { "overall", "reason", "evaluated_metric_count", "total_defined_dimension_count", "reasons" };

    private static readonly string[] RequiredReasonFields =
        { "metric", "label", "actual", "threshold", "rule", "severity" };

    private static readonly string[] RequiredPolicyFields =
        { "id", "name", "version", "path" };

    private static readonly string[] RequiredProvenanceFields =
        { "run_id", "started_at_utc", "completed_at_utc", "benchmark_version", "policy_id", "policy_version" };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
        { "PASS", "FAIL", "NOT_EVALUATED" };

    private static readonly HashSet<string> ValidDecisions = new(StringComparer.Ordinal)
        { "PASS", "PASS_WITH_CONDITIONS", "NOT_READY_FOR_DEPLOYMENT" };

    /// <summary>
    /// Validates a profile, returning every violation found (empty = valid).
    /// Collects all problems rather than stopping at the first, so a caller
    /// gets the full picture in one pass.
    /// </summary>
    public static IReadOnlyList<string> Validate(JsonObject profile)
    {
        var errors = new List<string>();

        foreach (var field in RequiredTopLevelFields)
            if (profile[field] is null)
                errors.Add($"missing required top-level field: {field}");

        if (profile["status_summary"] is JsonObject status)
        {
            foreach (var field in RequiredStatusSummaryFields)
                if (status[field] is null)
                    errors.Add($"status_summary missing field: {field}");
        }

        if (profile["metrics"] is JsonArray metrics)
        {
            for (int i = 0; i < metrics.Count; i++)
            {
                if (metrics[i] is not JsonObject m) { errors.Add($"metrics[{i}] is not an object"); continue; }
                foreach (var field in RequiredMetricFields)
                    if (!HasProperty(m, field))
                        errors.Add($"metrics[{i}] ('{(string?)m["metric"] ?? "?"}') missing field: {field}");

                var statusVal = (string?)m["status"];
                if (statusVal is not null && !ValidStatuses.Contains(statusVal))
                    errors.Add($"metrics[{i}] has invalid status: '{statusVal}'");
            }
        }
        else
        {
            errors.Add("metrics is missing or not an array");
        }

        if (profile["not_evaluated_dimensions"] is not JsonArray notEval)
            errors.Add("not_evaluated_dimensions is missing or not an array");
        else if (notEval.Any(d => d is not JsonValue))
            errors.Add("not_evaluated_dimensions must be an array of strings");

        if (profile["operational_capabilities"] is not JsonArray)
            errors.Add("operational_capabilities is missing or not an array");

        if (profile["deployment_decision"] is JsonObject decision)
        {
            foreach (var field in RequiredDecisionFields)
                if (!HasProperty(decision, field))
                    errors.Add($"deployment_decision missing field: {field}");

            var overall = (string?)decision["overall"];
            if (overall is not null && !ValidDecisions.Contains(overall))
                errors.Add($"deployment_decision.overall is not a recognised value: '{overall}'");

            if (decision["reasons"] is JsonArray reasons)
            {
                for (int i = 0; i < reasons.Count; i++)
                {
                    if (reasons[i] is not JsonObject r) { errors.Add($"deployment_decision.reasons[{i}] is not an object"); continue; }
                    foreach (var field in RequiredReasonFields)
                        if (!HasProperty(r, field))
                            errors.Add($"deployment_decision.reasons[{i}] missing field: {field}");
                }
            }
            else
            {
                errors.Add("deployment_decision.reasons is missing or not an array");
            }
        }
        else
        {
            errors.Add("deployment_decision is missing or not an object");
        }

        if (profile["policy"] is JsonObject policy)
        {
            foreach (var field in RequiredPolicyFields)
                if (!HasProperty(policy, field))
                    errors.Add($"policy missing field: {field}");
        }
        else
        {
            errors.Add("policy is missing or not an object");
        }

        if (profile["provenance"] is JsonObject provenance)
        {
            foreach (var field in RequiredProvenanceFields)
                if (!HasProperty(provenance, field))
                    errors.Add($"provenance missing field: {field}");
        }
        else
        {
            errors.Add("provenance is missing or not an object");
        }

        var hash = (string?)profile["result_hash"];
        if (string.IsNullOrEmpty(hash) || !hash.StartsWith("sha256:", StringComparison.Ordinal))
            errors.Add("result_hash is missing or not in 'sha256:<hex>' form");

        if (profile["provenance"]?["run_id"] is null)
            errors.Add("provenance.run_id is missing (every profile must be traceable to a run)");

        return errors;
    }

    /// <summary>Throws InvalidDataException with every violation listed if the profile is invalid. Does nothing if valid.</summary>
    public static void ValidateOrThrow(JsonObject profile)
    {
        var errors = Validate(profile);
        if (errors.Count > 0)
            throw new InvalidDataException(
                $"assurance_profile.json failed schema validation ({errors.Count} issue(s)):\n  - " + string.Join("\n  - ", errors));
    }

    /// <summary>
    /// A JSON property "has" a value here if the key exists at all, even if
    /// its value is JSON null -- e.g. a nullable metric value that's
    /// genuinely absent (NOT_EVALUATED) is still a present, valid field.
    /// Only a truly missing key is a schema violation.
    /// </summary>
    private static bool HasProperty(JsonObject obj, string field) =>
        obj.ContainsKey(field);
}
