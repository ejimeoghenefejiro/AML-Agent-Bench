using AmlAgent.Oracle;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// In-process tests for the C# reference oracle. Verifies the algorithm
/// produces a non-empty, schema-valid, well-ordered clusters file from the
/// bundled synthetic dataset. Runs from Visual Studio Test Explorer without
/// any environment setup.
/// </summary>
public class OracleSmokeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outPath;

    public OracleSmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aml-oracle-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _outPath = Path.Combine(_tempDir, "aml_clusters.csv");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static string FindTransfersCsv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "tasks", "aml-transaction-network", "environment", "data", "transfers.csv");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate transfers.csv by walking up from test base dir");
    }

    [Fact]
    public void OracleProducesAtLeastOneCluster()
    {
        var result = OracleRunner.Run(FindTransfersCsv(), _outPath);
        Assert.True(result.ClustersWritten > 0,
            "Oracle should produce at least one high-risk cluster from the bundled dataset");
        Assert.True(File.Exists(_outPath));
    }

    [Fact]
    public void OracleOutputIsSortedDescendingByRisk()
    {
        var result = OracleRunner.Run(FindTransfersCsv(), _outPath);
        for (int i = 1; i < result.Rows.Count; i++)
            Assert.True(result.Rows[i - 1].RiskScore >= result.Rows[i].RiskScore);
    }

    [Fact]
    public void OracleRiskScoresInValidRange()
    {
        var result = OracleRunner.Run(FindTransfersCsv(), _outPath);
        foreach (var row in result.Rows)
        {
            Assert.InRange(row.RiskScore, 0.65, 1.0);
            Assert.InRange(row.CircularFlowScore, 0.0, 1.0);
            Assert.True(row.AccountCount > 0);
            Assert.True(row.TotalValue > 0);
        }
    }

    [Fact]
    public void TopClusterHasMaximumRisk()
    {
        var result = OracleRunner.Run(FindTransfersCsv(), _outPath);
        Assert.NotEmpty(result.Rows);
        Assert.Equal(1.0, result.Rows[0].RiskScore, 4);
    }
}
