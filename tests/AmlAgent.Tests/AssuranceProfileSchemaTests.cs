using System.Text.Json.Nodes;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.AssuranceProfileSchema. Always-on, no
/// I/O -- builds minimal valid/invalid JsonObject fixtures directly.
/// </summary>
public class AssuranceProfileSchemaTests
{
    private static JsonObject MinimalValidProfile()
    {
        return new JsonObject
        {
            ["schema_version"] = "0.2",
            ["disclaimer"] = "test",
            ["generated_at_utc"] = "2026-01-01T00:00:00Z",
            ["status_summary"] = new JsonObject
            {
                ["execution_status"] = "completed",
                ["benchmark_verdict"] = "PASS",
                ["assurance_decision"] = "PASS",
            },
            ["agent"] = new JsonObject { ["name"] = "csharp-sk" },
            ["benchmark"] = "AML-Agent-Bench 0.1",
            ["scenario_pack"] = "task-006",
            ["operational_capabilities"] = new JsonArray("anomaly_detection"),
            ["jurisdiction_profile"] = "generic",
            ["policy"] = new JsonObject
            {
                ["id"] = "research-default",
                ["name"] = "default-illustrative",
                ["version"] = "1.0",
                ["path"] = "assurance/policy.default.json",
            },
            ["metrics"] = new JsonArray(new JsonObject
            {
                ["metric"] = "eghr_rate",
                ["label"] = "EGHR",
                ["value"] = 0.02,
                ["unit"] = "rate",
                ["threshold"] = 0.05,
                ["direction"] = "lower_is_better",
                ["required"] = true,
                ["status"] = "PASS",
            }),
            ["not_evaluated_dimensions"] = new JsonArray("Fairness disparity"),
            ["deployment_decision"] = new JsonObject
            {
                ["overall"] = "PASS",
                ["reason"] = "all good",
                ["evaluated_metric_count"] = 1,
                ["total_defined_dimension_count"] = 2,
                ["reasons"] = new JsonArray(),
            },
            ["provenance"] = new JsonObject
            {
                ["run_id"] = "abc123",
                ["started_at_utc"] = "2026-01-01T00:00:00Z",
                ["completed_at_utc"] = "2026-01-01T00:01:00Z",
                ["benchmark_version"] = "AML-Agent-Bench 0.1",
                ["policy_id"] = "research-default",
                ["policy_version"] = "1.0",
            },
            ["result_hash"] = "sha256:abcdef0123456789",
        };
    }

    [Fact]
    public void Validate_MinimalValidProfile_HasNoErrors()
    {
        var errors = AssuranceProfileSchema.Validate(MinimalValidProfile());
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingTopLevelField_IsReported()
    {
        var profile = MinimalValidProfile();
        profile.Remove("policy");
        var errors = AssuranceProfileSchema.Validate(profile);
        Assert.Contains(errors, e => e.Contains("policy"));
    }

    [Fact]
    public void Validate_MetricMissingRequiredField_IsReported()
    {
        var profile = MinimalValidProfile();
        ((JsonObject)profile["metrics"]![0]!).Remove("threshold");
        var errors = AssuranceProfileSchema.Validate(profile);
        Assert.Contains(errors, e => e.Contains("threshold"));
    }

    [Fact]
    public void Validate_InvalidMetricStatus_IsReported()
    {
        var profile = MinimalValidProfile();
        ((JsonObject)profile["metrics"]![0]!)["status"] = "MAYBE";
        var errors = AssuranceProfileSchema.Validate(profile);
        Assert.Contains(errors, e => e.Contains("invalid status"));
    }

    [Fact]
    public void Validate_UnrecognisedDecisionValue_IsReported()
    {
        var profile = MinimalValidProfile();
        ((JsonObject)profile["deployment_decision"]!)["overall"] = "SORT_OF_OK";
        var errors = AssuranceProfileSchema.Validate(profile);
        Assert.Contains(errors, e => e.Contains("overall"));
    }

    [Fact]
    public void Validate_DecisionReasonMissingField_IsReported()
    {
        var profile = MinimalValidProfile();
        ((JsonObject)profile["deployment_decision"]!)["reasons"] = new JsonArray(new JsonObject
        {
            ["metric"] = "eghr_rate",
            // missing label/actual/threshold/rule/severity
        });
        var errors = AssuranceProfileSchema.Validate(profile);
        Assert.Contains(errors, e => e.Contains("reasons[0]"));
    }

    [Fact]
    public void Validate_ProvenanceMissingRunId_IsReported()
    {
        var profile = MinimalValidProfile();
        ((JsonObject)profile["provenance"]!).Remove("run_id");
        var errors = AssuranceProfileSchema.Validate(profile);
        Assert.Contains(errors, e => e.Contains("run_id"));
    }

    [Fact]
    public void Validate_ResultHashWrongForm_IsReported()
    {
        var profile = MinimalValidProfile();
        profile["result_hash"] = "not-a-hash";
        var errors = AssuranceProfileSchema.Validate(profile);
        Assert.Contains(errors, e => e.Contains("result_hash"));
    }

    [Fact]
    public void ValidateOrThrow_InvalidProfile_ThrowsWithAllIssuesListed()
    {
        var profile = MinimalValidProfile();
        profile.Remove("policy");
        profile.Remove("provenance");
        var ex = Assert.Throws<InvalidDataException>(() => AssuranceProfileSchema.ValidateOrThrow(profile));
        Assert.Contains("policy", ex.Message);
        Assert.Contains("provenance", ex.Message);
    }

    [Fact]
    public void ValidateOrThrow_ValidProfile_DoesNotThrow()
    {
        AssuranceProfileSchema.ValidateOrThrow(MinimalValidProfile());
    }
}
