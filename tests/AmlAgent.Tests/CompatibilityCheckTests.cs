using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Unit tests for AmlAgent.Evidence.CompatibilityCheck (CLI-Only Assurance
/// Roadmap item 7). Always-on, no I/O.
/// </summary>
public class CompatibilityCheckTests
{
    private static RunIdentity Identity(string label, string task = "task-006", string policyId = "research-default",
        string policyVersion = "1.0", string benchmarkVersion = "AML-Agent-Bench 0.1", string datasetHash = "sha256:abc",
        string requiredDims = "sha256:def") =>
        new(label, task, policyId, policyVersion, benchmarkVersion, datasetHash, requiredDims);

    [Fact]
    public void Check_IdenticalRuns_NoWarnings()
    {
        var runs = new[] { Identity("a"), Identity("b") };
        Assert.Empty(CompatibilityCheck.Check(runs));
    }

    [Fact]
    public void Check_DifferentTask_WarnsAndNamesBothRuns()
    {
        var runs = new[] { Identity("a", task: "task-006"), Identity("b", task: "aml-transaction-network") };
        var warnings = CompatibilityCheck.Check(runs);
        Assert.Single(warnings);
        Assert.Contains("task", warnings[0]);
        Assert.Contains("'a'", warnings[0]);
        Assert.Contains("'b'", warnings[0]);
    }

    [Fact]
    public void Check_DifferentPolicy_Warns()
    {
        var runs = new[] { Identity("a", policyId: "research-default"), Identity("b", policyId: "bank-strict") };
        var warnings = CompatibilityCheck.Check(runs);
        Assert.Contains(warnings, w => w.Contains("policy id"));
    }

    [Fact]
    public void Check_DifferentDataset_Warns()
    {
        var runs = new[] { Identity("a", datasetHash: "sha256:aaa"), Identity("b", datasetHash: "sha256:bbb") };
        var warnings = CompatibilityCheck.Check(runs);
        Assert.Contains(warnings, w => w.Contains("dataset"));
    }

    [Fact]
    public void Check_MultipleDifferences_ReportsAllOfThem()
    {
        var runs = new[] { Identity("a", task: "task-006", policyId: "research-default"), Identity("b", task: "aml-transaction-network", policyId: "bank-strict") };
        var warnings = CompatibilityCheck.Check(runs);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void Check_UnknownFieldOnEitherSide_IsNotTreatedAsMismatch()
    {
        var a = Identity("a") with { DatasetHash = null };
        var b = Identity("b");
        var warnings = CompatibilityCheck.Check(new[] { a, b });
        Assert.DoesNotContain(warnings, w => w.Contains("dataset"));
    }

    [Fact]
    public void Check_SingleRun_NoWarnings()
    {
        Assert.Empty(CompatibilityCheck.Check(new[] { Identity("a") }));
    }

    [Fact]
    public void Check_ThreeRuns_ComparesEachAgainstTheFirst()
    {
        var runs = new[]
        {
            Identity("baseline", task: "task-006"),
            Identity("same-task", task: "task-006"),
            Identity("different-task", task: "aml-transaction-network"),
        };
        var warnings = CompatibilityCheck.Check(runs);
        Assert.Single(warnings);
        Assert.Contains("different-task", warnings[0]);
    }
}
