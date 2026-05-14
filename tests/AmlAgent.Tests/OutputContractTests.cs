using System.Globalization;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Schema, range and ordering tests for the agent's <c>aml_clusters.csv</c>
/// output. Run by the harness against a benchmark workspace identified by the
/// <c>AML_BENCH_WORKSPACE</c> environment variable. If that variable is not
/// set (e.g. when run directly from Visual Studio's Test Explorer with no
/// workspace), each test is skipped via SkippableFact.
/// </summary>
public class OutputContractTests
{
    private static readonly string[] ExpectedColumns =
        { "cluster_id", "account_count", "total_value", "circular_flow_score", "risk_score" };

    private static string? WorkspaceOutput()
    {
        var ws = Environment.GetEnvironmentVariable("AML_BENCH_WORKSPACE");
        if (string.IsNullOrEmpty(ws)) return null;
        return Path.Combine(ws, "aml_clusters.csv");
    }

    private static List<string[]> ReadCsv(string path) =>
        File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .ToList();

    [SkippableFact]
    public void OutputFileExists()
    {
        var path = WorkspaceOutput();
        Skip.If(path is null, "AML_BENCH_WORKSPACE not set");
        Assert.True(File.Exists(path), $"Expected output at {path}");
    }

    [SkippableFact]
    public void SchemaMatches()
    {
        var path = WorkspaceOutput();
        Skip.If(path is null || !File.Exists(path), "no workspace output");
        var rows = ReadCsv(path!);
        Assert.Equal(ExpectedColumns, rows[0]);
    }

    [SkippableFact]
    public void NoForbiddenTransactionColumns()
    {
        var path = WorkspaceOutput();
        Skip.If(path is null || !File.Exists(path), "no workspace output");
        var header = ReadCsv(path!)[0];
        foreach (var forbidden in new[] { "txn_id", "account_id", "source_account", "destination_account" })
            Assert.DoesNotContain(forbidden, header);
    }

    [SkippableFact]
    public void RiskScoresInRangeAndAboveThreshold()
    {
        var path = WorkspaceOutput();
        Skip.If(path is null || !File.Exists(path), "no workspace output");
        var rows = ReadCsv(path!);
        int riskCol = Array.IndexOf(rows[0], "risk_score");
        for (int i = 1; i < rows.Count; i++)
        {
            var risk = double.Parse(rows[i][riskCol], CultureInfo.InvariantCulture);
            Assert.InRange(risk, 0.65, 1.0);
        }
    }

    [SkippableFact]
    public void SortedByRiskDescendingThenClusterId()
    {
        var path = WorkspaceOutput();
        Skip.If(path is null || !File.Exists(path), "no workspace output");
        var rows = ReadCsv(path!);
        int idCol = 0, riskCol = Array.IndexOf(rows[0], "risk_score");
        var data = rows.Skip(1).ToList();
        for (int i = 1; i < data.Count; i++)
        {
            var prev = double.Parse(data[i - 1][riskCol], CultureInfo.InvariantCulture);
            var cur  = double.Parse(data[i][riskCol], CultureInfo.InvariantCulture);
            Assert.True(prev >= cur, $"Risk not descending at row {i}: {prev} then {cur}");
            if (prev == cur)
                Assert.True(string.CompareOrdinal(data[i - 1][idCol], data[i][idCol]) <= 0);
        }
    }

    [SkippableFact]
    public void NumericColumnsRoundedToFourDecimals()
    {
        var path = WorkspaceOutput();
        Skip.If(path is null || !File.Exists(path), "no workspace output");
        var rows = ReadCsv(path!);
        var indices = new[] { "total_value", "circular_flow_score", "risk_score" }
            .Select(c => Array.IndexOf(rows[0], c))
            .ToArray();
        for (int i = 1; i < rows.Count; i++)
        {
            foreach (var c in indices)
            {
                var raw = rows[i][c];
                var dot = raw.IndexOf('.');
                if (dot >= 0)
                    Assert.True(raw.Length - dot - 1 <= 4,
                        $"Value '{raw}' at col {c} has more than 4 decimal places");
            }
        }
    }
}
