namespace AmlAgent.Evidence;

/// <summary>
/// Pure logic behind CLI-Only Assurance Roadmap item 7: detecting when a
/// `compare` or `regress` invocation spans runs that aren't actually
/// equivalent (different task, task version, policy, benchmark version, or
/// dataset), so the comparison can be clearly labelled non-equivalent
/// rather than silently presented as apples-to-apples. No I/O -- callers
/// extract a RunIdentity from each assurance_profile.json and pass it in.
/// </summary>
public static class CompatibilityCheck
{
    /// <summary>
    /// Compares every run against the first (as the reference) and returns
    /// one warning per dimension that differs. Empty list means every run
    /// is comparable on every tracked dimension. Null fields on either side
    /// are treated as "unknown", not a mismatch, since an old-schema
    /// profile predating a field shouldn't spuriously fail the check.
    /// </summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<RunIdentity> runs)
    {
        var warnings = new List<string>();
        if (runs.Count < 2) return warnings;

        var reference = runs[0];
        for (int i = 1; i < runs.Count; i++)
        {
            var run = runs[i];
            CompareField(warnings, reference, run, "task", r => r.Task);
            CompareField(warnings, reference, run, "policy id", r => r.PolicyId);
            CompareField(warnings, reference, run, "policy version", r => r.PolicyVersion);
            CompareField(warnings, reference, run, "benchmark version", r => r.BenchmarkVersion);
            CompareField(warnings, reference, run, "dataset", r => r.DatasetHash);
            CompareField(warnings, reference, run, "required assurance dimensions", r => r.RequiredDimensionsFingerprint);
        }
        return warnings;
    }

    private static void CompareField(List<string> warnings, RunIdentity reference, RunIdentity run, string fieldName, Func<RunIdentity, string?> selector)
    {
        var a = selector(reference);
        var b = selector(run);
        if (a is null || b is null) return; // unknown on either side -- not a mismatch, just unrecorded
        if (a != b)
            warnings.Add($"'{reference.Label}' and '{run.Label}' differ in {fieldName} ('{a}' vs '{b}') -- comparison is non-equivalent");
    }
}

/// <summary>The identity fields of one run relevant to deciding whether it's comparable to another.</summary>
public sealed record RunIdentity(
    string Label,
    string? Task,
    string? PolicyId,
    string? PolicyVersion,
    string? BenchmarkVersion,
    string? DatasetHash,
    string? RequiredDimensionsFingerprint);
