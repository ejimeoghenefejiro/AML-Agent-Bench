using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Structural validation of the candidate's
/// <c>temporal_anomaly_summary.csv</c> + <c>temporal_anomaly_report.md</c>
/// for Task 006. Runs against the harness-staged workspace identified by
/// <c>AML_BENCH_WORKSPACE</c>; skipped when no workspace is set or when the
/// summary file is missing (e.g. running against a Task 001 workspace).
/// </summary>
public class Task006SummaryTests
{
    private static readonly string[] ExpectedColumns =
    {
        "week", "start_date", "end_date", "transfer_count", "unique_accounts",
        "total_value", "new_accounts_count", "high_risk_dest_count", "sar_count",
        "anomaly_score"
    };

    private static string? SummaryPath()
    {
        var ws = Environment.GetEnvironmentVariable("AML_BENCH_WORKSPACE");
        if (string.IsNullOrEmpty(ws)) return null;
        var p = Path.Combine(ws, "temporal_anomaly_summary.csv");
        return File.Exists(p) ? p : null;
    }

    private static string? ReportPath()
    {
        var ws = Environment.GetEnvironmentVariable("AML_BENCH_WORKSPACE");
        if (string.IsNullOrEmpty(ws)) return null;
        var p = Path.Combine(ws, "temporal_anomaly_report.md");
        return File.Exists(p) ? p : null;
    }

    private static List<string[]> ReadCsv(string path) =>
        File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .ToList();

    [SkippableFact]
    public void SummaryExists()
    {
        var ws = Environment.GetEnvironmentVariable("AML_BENCH_WORKSPACE");
        Skip.If(string.IsNullOrEmpty(ws), "no workspace");
        Skip.If(!File.Exists(Path.Combine(ws!, "prompt.md"))
             && !File.Exists(Path.Combine(ws!, "instruction.md")), "no task in workspace");
        Skip.If(!File.Exists(Path.Combine(ws!, "data", "weekly_transfers.csv")), "not Task 006");

        var p = Path.Combine(ws!, "temporal_anomaly_summary.csv");
        Assert.True(File.Exists(p), $"Expected {p}");
    }

    [SkippableFact]
    public void SummarySchemaMatches()
    {
        var p = SummaryPath();
        Skip.If(p is null, "no summary");
        Assert.Equal(ExpectedColumns, ReadCsv(p!)[0]);
    }

    [SkippableFact]
    public void SummaryHasThreeOrderedWeeks()
    {
        var p = SummaryPath();
        Skip.If(p is null, "no summary");
        var rows = ReadCsv(p!);
        var data = rows.Skip(1).ToList();
        Assert.Equal(3, data.Count);
        var weekCol = Array.IndexOf(rows[0], "week");
        Assert.Equal("week_1", data[0][weekCol]);
        Assert.Equal("week_2", data[1][weekCol]);
        Assert.Equal("week_3", data[2][weekCol]);
    }

    [SkippableFact]
    public void AnomalyScoresInRange()
    {
        var p = SummaryPath();
        Skip.If(p is null, "no summary");
        var rows = ReadCsv(p!);
        var col = Array.IndexOf(rows[0], "anomaly_score");
        foreach (var r in rows.Skip(1))
            Assert.InRange(double.Parse(r[col], CultureInfo.InvariantCulture), 0.0, 1.0);
    }

    [SkippableFact]
    public void AnomalyScoreStrictlyIncreasing()
    {
        var p = SummaryPath();
        Skip.If(p is null, "no summary");
        var rows = ReadCsv(p!);
        var col = Array.IndexOf(rows[0], "anomaly_score");
        var data = rows.Skip(1).ToList();
        var w1 = double.Parse(data[0][col], CultureInfo.InvariantCulture);
        var w2 = double.Parse(data[1][col], CultureInfo.InvariantCulture);
        var w3 = double.Parse(data[2][col], CultureInfo.InvariantCulture);
        Assert.True(w1 < w2, $"expected week_1 ({w1}) < week_2 ({w2})");
        Assert.True(w2 < w3, $"expected week_2 ({w2}) < week_3 ({w3})");
        Assert.True(w3 >= 0.7, $"expected week_3 anomaly_score >= 0.7, got {w3}");
    }

    [SkippableFact]
    public void NumericValuesRoundedToFourDecimals()
    {
        var p = SummaryPath();
        Skip.If(p is null, "no summary");
        var rows = ReadCsv(p!);
        var numericCols = new[] { "total_value", "anomaly_score" };
        var idxs = numericCols.Select(c => Array.IndexOf(rows[0], c)).ToArray();
        for (int i = 1; i < rows.Count; i++)
            foreach (var c in idxs)
            {
                var raw = rows[i][c];
                var dot = raw.IndexOf('.');
                if (dot >= 0)
                    Assert.True(raw.Length - dot - 1 <= 4,
                        $"value '{raw}' at col {c} has >4 decimal places");
            }
    }

    [SkippableFact]
    public void ReportExistsAndCitesTransactionIds()
    {
        var p = ReportPath();
        Skip.If(p is null, "no report");
        var content = File.ReadAllText(p!);
        Assert.False(string.IsNullOrWhiteSpace(content), "report is empty");
        var citations = Regex.Matches(content, @"\bT[123]-\d{3}\b").Count;
        Assert.True(citations >= 3,
            $"expected at least 3 transaction-ID citations like T2-014, found {citations}");
    }
}
