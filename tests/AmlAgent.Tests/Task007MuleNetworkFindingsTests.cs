using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace AmlAgent.Tests;

/// <summary>
/// Structural validation of the candidate's <c>mule_network_findings.csv</c> +
/// <c>mule_network_report.md</c> for Task 007 -- the multi-source case task.
/// Runs against the harness-staged workspace identified by
/// <c>AML_BENCH_WORKSPACE</c>; skipped when no workspace is set or when the
/// findings file is missing (e.g. running against a different task's
/// workspace). The core assertions (rows 5-8) are the machine-checkable half
/// of this task's real point: does the agent correctly separate the actual
/// mule network from accounts that merely co-occur in the same data.
/// </summary>
public class Task007MuleNetworkFindingsTests
{
    private static readonly string[] ExpectedColumns = { "account_id", "classification", "confidence", "supporting_txn_ids" };
    private static readonly string[] ValidClassifications = { "victim", "mule", "exit_point", "watchlist_match", "cleared" };

    private static string? Workspace() => Environment.GetEnvironmentVariable("AML_BENCH_WORKSPACE");

    private static string? FindingsPath()
    {
        var ws = Workspace();
        if (string.IsNullOrEmpty(ws)) return null;
        var p = Path.Combine(ws, "mule_network_findings.csv");
        return File.Exists(p) ? p : null;
    }

    private static string? ReportPath()
    {
        var ws = Workspace();
        if (string.IsNullOrEmpty(ws)) return null;
        var p = Path.Combine(ws, "mule_network_report.md");
        return File.Exists(p) ? p : null;
    }

    private static List<string[]> ReadCsv(string path) =>
        File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .ToList();

    private static Dictionary<string, string[]> RowsByAccountId(string path)
    {
        var rows = ReadCsv(path);
        var accountCol = Array.IndexOf(rows[0], "account_id");
        return rows.Skip(1).ToDictionary(r => r[accountCol], r => r, StringComparer.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void FindingsExists()
    {
        var ws = Workspace();
        Skip.If(string.IsNullOrEmpty(ws), "no workspace");
        Skip.If(!File.Exists(Path.Combine(ws!, "case-definition.json")) &&
                 !File.Exists(Path.Combine(ws!, "case_manifest.json")), "not Task 007 (no multi-source case in this workspace)");

        var p = Path.Combine(ws!, "mule_network_findings.csv");
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
    public void ConfidenceValuesInRange()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var rows = ReadCsv(p!);
        var col = Array.IndexOf(rows[0], "confidence");
        foreach (var r in rows.Skip(1))
            Assert.InRange(double.Parse(r[col], CultureInfo.InvariantCulture), 0.0, 1.0);
    }

    [SkippableFact]
    public void VictimIsCorrectlyIdentified()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var byId = RowsByAccountId(p!);
        var col = Array.IndexOf(ReadCsv(p!)[0], "classification");
        Assert.True(byId.ContainsKey("N100"), "expected a row for N100 (the victim)");
        Assert.Equal("victim", byId["N100"][col]);
    }

    [SkippableFact]
    public void MuleLayerAccountsAreCorrectlyIdentified()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var byId = RowsByAccountId(p!);
        var col = Array.IndexOf(ReadCsv(p!)[0], "classification");
        foreach (var mule in new[] { "M201", "M202", "M301" })
        {
            Assert.True(byId.ContainsKey(mule), $"expected a row for {mule}");
            Assert.Equal("mule", byId[mule][col]);
        }
    }

    [SkippableFact]
    public void ExitAccountIsCorrectlyIdentified()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var byId = RowsByAccountId(p!);
        var col = Array.IndexOf(ReadCsv(p!)[0], "classification");
        Assert.True(byId.ContainsKey("EXT401"), "expected a row for EXT401 (the exit account)");
        Assert.Equal("exit_point", byId["EXT401"][col]);
    }

    [SkippableFact]
    public void InnocentAccountsAreNotFalselyImplicated()
    {
        var p = FindingsPath();
        Skip.If(p is null, "no findings file");
        var byId = RowsByAccountId(p!);
        var col = Array.IndexOf(ReadCsv(p!)[0], "classification");
        var implicating = new[] { "mule", "exit_point", "watchlist_match" };

        foreach (var innocent in new[] { "N150", "N160" })
        {
            if (!byId.TryGetValue(innocent, out var row)) continue; // absence is a judge-scored quality issue, not a hard failure here
            Assert.DoesNotContain(row[col], implicating);
        }
    }

    [SkippableFact]
    public void ReportExistsAndCitesGoldTransactionIds()
    {
        var p = ReportPath();
        Skip.If(p is null, "no report");
        var content = File.ReadAllText(p!);
        Assert.False(string.IsNullOrWhiteSpace(content), "report is empty");
        var citations = Regex.Matches(content, @"\bT[12]-\d{3}\b").Count;
        Assert.True(citations >= 3, $"expected at least 3 transaction-ID citations like T1-003, found {citations}");
    }

    [SkippableFact]
    public void CaseManifestWasGeneratedForThisWorkspace()
    {
        var ws = Workspace();
        Skip.If(string.IsNullOrEmpty(ws), "no workspace");
        Skip.If(!File.Exists(Path.Combine(ws!, "case-definition.json")), "not Task 007");

        var manifestPath = Path.Combine(ws!, "case_manifest.json");
        Assert.True(File.Exists(manifestPath), "expected case_manifest.json to be generated by StageCanonicalCaseIfPresent");
    }
}
