using System.Globalization;
using System.Text;

namespace AmlAgent.Oracle;

/// <summary>
/// Pure-C# reference implementation of the AML clustering pipeline previously
/// expressed in solve.py (pandas + networkx). Reading this is the canonical
/// definition of the task's expected output.
/// </summary>
public static class OracleRunner
{
    public sealed record Transfer(
        string TxnId, DateTime Timestamp, string Source, string Destination,
        double Amount, bool SarLinked);

    public sealed record OutputRow(
        string ClusterId, int AccountCount, double TotalValue,
        double CircularFlowScore, double RiskScore);

    public sealed record Result(int ClustersWritten, string OutputPath, IReadOnlyList<OutputRow> Rows);

    public static Result Run(string inputCsvPath, string outputCsvPath)
    {
        var transfers = ReadTransfers(inputCsvPath);

        var g = new AmlGraph();
        foreach (var t in transfers) g.AddTransfer(t.Source, t.Destination, t.Amount);

        // Deterministic component ordering: sort by lexicographically-smallest
        // member so cluster ids are stable run-to-run, then index 1..N.
        var components = g.WeaklyConnectedComponents()
            .OrderBy(c => c.Min(StringComparer.Ordinal), StringComparer.Ordinal)
            .ToList();

        var metrics = new List<ClusterMetrics>();
        int clusterIndex = 1;
        foreach (var comp in components)
        {
            var subEdges = new List<(string Src, string Dst)>();
            foreach (var src in comp)
            {
                if (!g.Adjacency.TryGetValue(src, out var inner)) continue;
                foreach (var dst in inner.Keys)
                    if (comp.Contains(dst))
                        subEdges.Add((src, dst));
            }
            int edgeCount = Math.Max(subEdges.Count, 1);

            var sccId = g.StronglyConnectedComponents(comp);
            var sccSize = new Dictionary<int, int>();
            foreach (var v in sccId.Values)
                sccSize[v] = sccSize.GetValueOrDefault(v) + 1;

            int cycleEdges = 0;
            foreach (var (src, dst) in subEdges)
                if (src != dst
                    && sccId[src] == sccId[dst]
                    && sccSize[sccId[src]] >= 2)
                    cycleEdges++;

            double circular = (double)cycleEdges / edgeCount;

            var clusterRows = transfers
                .Where(t => comp.Contains(t.Source) && comp.Contains(t.Destination))
                .ToList();

            double totalValue = clusterRows.Sum(t => t.Amount);
            double sarRatio = clusterRows.Count == 0
                ? 0.0
                : clusterRows.Average(t => t.SarLinked ? 1.0 : 0.0);

            DateTime minTs = clusterRows.Count == 0 ? DateTime.MinValue : clusterRows.Min(t => t.Timestamp);
            DateTime maxTs = clusterRows.Count == 0 ? DateTime.MinValue : clusterRows.Max(t => t.Timestamp);
            double spanHours = Math.Max((maxTs - minTs).TotalHours, 0.01);
            double velocity = clusterRows.Count / spanHours;

            metrics.Add(new ClusterMetrics(
                ClusterId: $"cluster_{clusterIndex}",
                AccountCount: comp.Count,
                TotalValue: totalValue,
                CircularFlow: circular,
                SarRatio: sarRatio,
                Velocity: velocity));
            clusterIndex++;
        }

        var valueComp = MinMax(metrics.Select(m => m.TotalValue).ToArray());
        var accountComp = MinMax(metrics.Select(m => (double)m.AccountCount).ToArray());
        var velocityComp = MinMax(metrics.Select(m => m.Velocity).ToArray());
        var circularComp = metrics.Select(m => Math.Clamp(m.CircularFlow, 0, 1)).ToArray();
        var sarComp = metrics.Select(m => Math.Clamp(m.SarRatio, 0, 1)).ToArray();

        var rawRisk = new double[metrics.Count];
        for (int i = 0; i < metrics.Count; i++)
        {
            rawRisk[i] = 0.35 * valueComp[i]
                       + 0.25 * circularComp[i]
                       + 0.15 * accountComp[i]
                       + 0.15 * velocityComp[i]
                       + 0.10 * sarComp[i];
        }
        var risk = MinMax(rawRisk);

        var output = new List<OutputRow>();
        for (int i = 0; i < metrics.Count; i++)
        {
            if (risk[i] < 0.65) continue;
            output.Add(new OutputRow(
                metrics[i].ClusterId,
                metrics[i].AccountCount,
                Math.Round(metrics[i].TotalValue, 4),
                Math.Round(metrics[i].CircularFlow, 4),
                Math.Round(risk[i], 4)));
        }

        output = output
            .OrderByDescending(r => r.RiskScore)
            .ThenBy(r => r.ClusterId, StringComparer.Ordinal)
            .ToList();

        WriteOutput(outputCsvPath, output);
        return new Result(output.Count, outputCsvPath, output);
    }

    private sealed record ClusterMetrics(
        string ClusterId, int AccountCount, double TotalValue,
        double CircularFlow, double SarRatio, double Velocity);

    private static double[] MinMax(double[] xs)
    {
        if (xs.Length == 0) return xs;
        double mn = xs.Min(), mx = xs.Max();
        if (mn == mx) return xs.Select(_ => 0.0).ToArray();
        return xs.Select(x => (x - mn) / (mx - mn)).ToArray();
    }

    private static List<Transfer> ReadTransfers(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) throw new InvalidDataException("Empty transfers file");

        var header = lines[0].Split(',');
        int Col(string name) =>
            Array.IndexOf(header, name) is var idx && idx >= 0
                ? idx
                : throw new InvalidDataException($"Missing column: {name}");

        int cTxn = Col("txn_id");
        int cTs = Col("timestamp");
        int cSrc = Col("source_account");
        int cDst = Col("destination_account");
        int cAmt = Col("amount");
        int cSar = Col("sar_linked");

        var rows = new List<Transfer>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split(',');
            rows.Add(new Transfer(
                TxnId: f[cTxn],
                Timestamp: DateTime.Parse(f[cTs], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                Source: f[cSrc],
                Destination: f[cDst],
                Amount: double.Parse(f[cAmt], CultureInfo.InvariantCulture),
                SarLinked: int.Parse(f[cSar], CultureInfo.InvariantCulture) != 0));
        }
        return rows;
    }

    private static void WriteOutput(string path, IEnumerable<OutputRow> rows)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.AppendLine("cluster_id,account_count,total_value,circular_flow_score,risk_score");
        foreach (var r in rows)
        {
            sb.Append(r.ClusterId).Append(',')
              .Append(r.AccountCount.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(r.TotalValue.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.CircularFlowScore.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.RiskScore.ToString("0.####", CultureInfo.InvariantCulture))
              .Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }
}
