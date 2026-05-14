namespace AmlAgent.Oracle;

/// <summary>
/// Minimal directed weighted multigraph used by the AML reference oracle.
/// Each ordered (src, dst) pair is a single edge; repeated transfers accumulate
/// into the edge's Weight and Count. Implements the two graph operations the
/// oracle needs: weakly connected components and Tarjan strongly connected
/// components on an induced sub-vertex-set.
/// </summary>
public sealed class AmlGraph
{
    public sealed class Edge { public double Weight; public int Count; }

    public readonly Dictionary<string, Dictionary<string, Edge>> Adjacency = new();
    public readonly HashSet<string> Nodes = new();

    public void AddTransfer(string src, string dst, double amount)
    {
        Nodes.Add(src);
        Nodes.Add(dst);
        if (!Adjacency.TryGetValue(src, out var inner))
        {
            inner = new Dictionary<string, Edge>();
            Adjacency[src] = inner;
        }
        if (!inner.TryGetValue(dst, out var edge))
        {
            edge = new Edge();
            inner[dst] = edge;
        }
        edge.Weight += amount;
        edge.Count++;
    }

    public List<HashSet<string>> WeaklyConnectedComponents()
    {
        var parent = new Dictionary<string, string>();
        foreach (var n in Nodes) parent[n] = n;

        string Find(string x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }
        void Union(string a, string b)
        {
            var ra = Find(a); var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        foreach (var (src, inner) in Adjacency)
            foreach (var dst in inner.Keys)
                Union(src, dst);

        var groups = new Dictionary<string, HashSet<string>>();
        foreach (var n in Nodes)
        {
            var r = Find(n);
            if (!groups.TryGetValue(r, out var bucket))
            {
                bucket = new HashSet<string>();
                groups[r] = bucket;
            }
            bucket.Add(n);
        }
        return groups.Values.ToList();
    }

    /// <summary>
    /// Tarjan's SCC algorithm restricted to the given vertex set. Returns a
    /// mapping from each vertex to its SCC id. Iterative implementation to
    /// avoid stack overflow on pathological graphs.
    /// </summary>
    public Dictionary<string, int> StronglyConnectedComponents(HashSet<string> subNodes)
    {
        var index = 0;
        var idx = new Dictionary<string, int>();
        var low = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var stack = new Stack<string>();
        var sccId = new Dictionary<string, int>();
        var nextScc = 0;

        var callStack = new Stack<(string Node, IEnumerator<string> NeighborEnum)>();

        IEnumerator<string> NeighborsOf(string v)
        {
            if (!Adjacency.TryGetValue(v, out var inner)) yield break;
            foreach (var w in inner.Keys)
                if (subNodes.Contains(w)) yield return w;
        }

        foreach (var start in subNodes)
        {
            if (idx.ContainsKey(start)) continue;

            idx[start] = index;
            low[start] = index;
            index++;
            stack.Push(start);
            onStack.Add(start);
            callStack.Push((start, NeighborsOf(start)));

            while (callStack.Count > 0)
            {
                var (v, it) = callStack.Peek();
                if (it.MoveNext())
                {
                    var w = it.Current;
                    if (!idx.ContainsKey(w))
                    {
                        idx[w] = index;
                        low[w] = index;
                        index++;
                        stack.Push(w);
                        onStack.Add(w);
                        callStack.Push((w, NeighborsOf(w)));
                    }
                    else if (onStack.Contains(w))
                    {
                        if (idx[w] < low[v]) low[v] = idx[w];
                    }
                }
                else
                {
                    callStack.Pop();
                    if (low[v] == idx[v])
                    {
                        while (true)
                        {
                            var w = stack.Pop();
                            onStack.Remove(w);
                            sccId[w] = nextScc;
                            if (w == v) break;
                        }
                        nextScc++;
                    }
                    if (callStack.Count > 0)
                    {
                        var (parentNode, _) = callStack.Peek();
                        if (low[v] < low[parentNode]) low[parentNode] = low[v];
                    }
                }
            }
        }
        return sccId;
    }
}
