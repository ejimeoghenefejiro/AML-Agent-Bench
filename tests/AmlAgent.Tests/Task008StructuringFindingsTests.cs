using System.Text.RegularExpressions;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Structural validation of the candidate's <c>structuring_findings.csv</c> +
/// <c>structuring_report.md</c> for Task 008 -- the level-2 (multi-record
/// aggregation) task: no single transaction is suspicious on its own, but
/// six of them together are. Runs against the harness-staged workspace
/// identified by <c>AML_BENCH_WORKSPACE</c>; skipped when no workspace is
/// set or when the findings file is missing (e.g. running against a
/// different task's workspace).
/// </summary>
public class Task008StructuringFindingsTests
{
    private static readonly string[] ExpectedColumns = { "txn_id", "classification", "amount", "supporting_txn_ids" };
    private static readonly string[] ValidClassifications = { "structuring_component", "unrelated" };
    private static readonly string[] AllSourceTxnIds =
        { "T1-001", "T1-002", "T1-003", "T1-004", "T1-005", "T1-006", "T1-007", "T1-008" };
    private static readonly string[] StructuringTxnIds =
        { "T1-001", "T1-002", "T1-003", "T1-004", "T1-005", "T1-006" };
    private static readonly string[] DistractorTxnIds = { "T1-007", "T1-008" };

    private static string? Workspace() => Environment.GetEnvironmentVariable("AML_BENCH_WORKSPACE");

    private static string? FindingsPath()
    {
        var ws = Workspace();
        if (string.IsNullOrEmpty(ws)) return null;
        var p = Path.Combine(ws, "structuring_findings.csv");
        return File.Exists(p) ? p : null;
    }

    private static string? ReportPath()
    {
        var ws = Workspace();
        if (string.IsNullOrEmpty(ws)) return null;
        var p = Path.Combine(ws, "structuring_report.md");
        return File.Exists(p) ? p : null;
    }

    private static List<string[]> ReadCsv(string path) =>
        File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .ToList();

    private static Dictionary<string, string[]> RowsByTxnId(string path)
    {
        var rows = ReadCsv(path);
        var col = Array.IndexOf(rows[0], "txn_id");
        return rows.Skip(1).ToDictionary(r => r[col], r => r, StringComparer.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void FindingsExists()
    {
        var ws = Workspace();
        Skip.If(string.IsNullOrEmpty(ws), "no workspace");
        Skip.If(!File.Exists(Path.Combine(ws!, "data", "structuring_transfers.csv")), "not Task 008");

        var p = Path.Combine(ws!, "structuring_findings.csv");
        Assert.True(File.Exists(p), $"Expected {p}");
    }

    [SkippableFact]
    public void FindingsSchemaMatches()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        Assert.Equal(ExpectedColumns, ReadCsv(p!)[0]);
    }

    [SkippableFact]
    public void EveryClassificationIsAValidValue()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var rows = ReadCsv(p!);
        var col = Array.IndexOf(rows[0], "classification");
        foreach (var r in rows.Skip(1))
            Assert.Contains(r[col], ValidClassifications);
    }

    [SkippableFact]
    public void EveryTransactionAppearsExactlyOnce()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var byId = RowsByTxnId(p!);
        foreach (var id in AllSourceTxnIds)
            Assert.True(byId.ContainsKey(id), $"expected a row for {id}");
        Assert.Equal(AllSourceTxnIds.Length, ReadCsv(p!).Count - 1);
    }

    [SkippableFact]
    public void AllSixStructuringTransactionsAreCorrectlyIdentified()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var byId = RowsByTxnId(p!);
        var col = Array.IndexOf(ReadCsv(p!)[0], "classification");
        foreach (var id in StructuringTxnIds)
        {
            Assert.True(byId.ContainsKey(id), $"expected a row for {id}");
            Assert.Equal("structuring_component", byId[id][col]);
        }
    }

    [SkippableFact]
    public void BothDistractorsAreCorrectlyCleared()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var byId = RowsByTxnId(p!);
        var col = Array.IndexOf(ReadCsv(p!)[0], "classification");
        foreach (var id in DistractorTxnIds)
        {
            Assert.True(byId.ContainsKey(id), $"expected a row for {id}");
            Assert.Equal("unrelated", byId[id][col]);
        }
    }

    [SkippableFact]
    public void ReportExistsAndCitesGoldTransactionIds()
    {
        var p = ReportPath();
        Skip.If(p is null, "no report");
        var content = File.ReadAllText(p!);
        Assert.False(string.IsNullOrWhiteSpace(content), "report is empty");
        var citations = Regex.Matches(content, @"\bT1-\d{3}\b").Count;
        Assert.True(citations >= 3, $"expected at least 3 transaction-ID citations like T1-003, found {citations}");
    }
}
