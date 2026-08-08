namespace AmlAgent.Evidence;

/// <summary>
/// Pure decision logic for turning a set of measured metrics into an
/// AML Agent Assurance Profile deployment decision, per the "AML-Agent-Bench
/// Real-World Assurance Profile" vision and its CLI-Only Assurance Roadmap
/// follow-up: measured metrics vs. a policy's thresholds, each threshold
/// marked required (a "critical" gate — failing it blocks deployment
/// outright) or optional (a "warning" — failing it downgrades to
/// PASS_WITH_CONDITIONS rather than blocking), with an honest accounting of
/// which assurance dimensions were not evaluated at all rather than
/// silently omitting them.
///
/// No I/O here — callers (the harness) load the policy file, gather metric
/// values from bench_result.json, and pass plain values in. This keeps the
/// decision rule itself unit-testable without a workspace or a live run.
/// </summary>
public static class AssuranceEngine
{
    private static readonly HashSet<string> ValidDirections = new(StringComparer.Ordinal)
    {
        "higher_is_better", "lower_is_better",
    };

    /// <summary>
    /// Rejects a policy that can't produce a sound decision: unknown
    /// direction, or a "rate" metric whose threshold sits outside the [0,1]
    /// range it's supposed to bound. Called once when a policy is loaded,
    /// so a bad policy fails loudly at load time rather than silently
    /// producing a nonsensical PASS/FAIL later.
    /// </summary>
    public static void ValidatePolicy(IReadOnlyList<MetricThreshold> thresholds)
    {
        foreach (var t in thresholds)
        {
            if (!ValidDirections.Contains(t.Direction))
                throw new InvalidDataException($"policy threshold '{t.Metric}': unknown direction '{t.Direction}' (expected higher_is_better or lower_is_better)");

            if (t.Unit == "rate" && (t.Threshold < 0.0 || t.Threshold > 1.0))
                throw new InvalidDataException($"policy threshold '{t.Metric}': impossible threshold {t.Threshold} for a rate metric (must be between 0 and 1)");

            if (t.Unit == "count" && t.Threshold < 0.0)
                throw new InvalidDataException($"policy threshold '{t.Metric}': impossible threshold {t.Threshold} for a count metric (must be >= 0)");
        }
    }

    public static MetricResult EvaluateMetric(MetricThreshold threshold, double? value)
    {
        if (value is null)
            return new MetricResult(threshold.Metric, threshold.Label, null, threshold.Unit, threshold, "NOT_EVALUATED");

        bool pass = threshold.Direction switch
        {
            "higher_is_better" => value.Value >= threshold.Threshold,
            "lower_is_better" => value.Value <= threshold.Threshold,
            _ => throw new ArgumentException($"unknown threshold direction: {threshold.Direction}"),
        };

        return new MetricResult(threshold.Metric, threshold.Label, value, threshold.Unit, threshold, pass ? "PASS" : "FAIL");
    }

    /// <summary>
    /// A required ("critical") metric failing blocks deployment outright.
    /// An optional ("warning") metric failing, or any dimension this
    /// benchmark simply doesn't measure yet, downgrades the decision to
    /// PASS_WITH_CONDITIONS instead of a bare PASS — the gap is visible in
    /// the decision itself and in structured Reasons, not just a field
    /// nobody reads.
    /// </summary>
    public static AssuranceDecision Decide(
        IReadOnlyList<MetricResult> results,
        IReadOnlyList<string> notImplementedDimensions)
    {
        var evaluated = results.Where(r => r.Status is "PASS" or "FAIL").ToList();
        var failed = evaluated.Where(r => r.Status == "FAIL").ToList();
        var requiredFailed = failed.Where(r => r.Threshold?.Required != false).ToList();
        var optionalFailed = failed.Where(r => r.Threshold?.Required == false).ToList();

        var notEvaluated = results.Where(r => r.Status == "NOT_EVALUATED").Select(r => r.Label)
            .Concat(notImplementedDimensions)
            .Distinct()
            .ToList();

        var reasons = failed.Select(f => new DecisionReason(
            Metric: f.Metric,
            Label: f.Label,
            Actual: f.Value,
            Threshold: f.Threshold?.Threshold ?? 0,
            Rule: f.Threshold?.Direction == "higher_is_better" ? "minimum" : "maximum",
            Severity: f.Threshold?.Required != false ? "critical" : "warning")).ToList();

        if (evaluated.Count == 0)
        {
            return new AssuranceDecision(
                "NOT_READY_FOR_DEPLOYMENT", results, 0, results.Count + notImplementedDimensions.Count,
                notEvaluated, reasons,
                "No policy metrics could be evaluated against this run's data.");
        }

        if (requiredFailed.Count > 0)
        {
            var names = string.Join(", ", requiredFailed.Select(f => f.Label));
            return new AssuranceDecision(
                "NOT_READY_FOR_DEPLOYMENT", results, evaluated.Count, results.Count + notImplementedDimensions.Count,
                notEvaluated, reasons,
                $"Failed required (critical) policy threshold(s): {names}.");
        }

        var gaps = new List<string>();
        if (optionalFailed.Count > 0)
            gaps.Add($"optional threshold(s) breached: {string.Join(", ", optionalFailed.Select(f => f.Label))}");
        if (notEvaluated.Count > 0)
            gaps.Add($"not evaluated: {string.Join(", ", notEvaluated)}");

        if (gaps.Count > 0)
        {
            return new AssuranceDecision(
                "PASS_WITH_CONDITIONS", results, evaluated.Count, results.Count + notImplementedDimensions.Count,
                notEvaluated, reasons,
                $"All required metrics passed, but {string.Join("; ", gaps)}. Deployment should be conditioned on those gaps being assessed by other means.");
        }

        return new AssuranceDecision(
            "PASS", results, evaluated.Count, results.Count,
            notEvaluated, reasons,
            "All defined policy metrics passed.");
    }

    /// <summary>
    /// Case-level evidence integrity (CanonicalCaseMerger/EvidenceIntegrityValidator's
    /// output, surfaced via case_manifest.json) is a distinct question from the
    /// judge-metric decision above: EGHR / fabricated citations / missing gold
    /// evidence all ask "did the agent's report stay grounded in the evidence it
    /// was given"; this asks "was the evidence itself trustworthy" -- independent
    /// of anything the agent said. invalidSourceEvidenceReferenceCount covers
    /// dangling/missing/wrong-typed references; brokenCanonicalEvidenceLineageCount
    /// covers the same evidence id being contributed with conflicting content by
    /// more than one source (no single authoritative lineage for it). A task with
    /// no multi-source case (casePresent=false) produces no reasons at all -- no
    /// behaviour change for tasks that never had a case_manifest.json.
    /// </summary>
    public static CaseIntegrityAssessment EvaluateCaseIntegrity(
        bool casePresent,
        int invalidSourceEvidenceReferenceCount,
        int brokenCanonicalEvidenceLineageCount)
    {
        if (!casePresent)
            return new CaseIntegrityAssessment(false, 0, 0, Array.Empty<DecisionReason>());

        var reasons = new List<DecisionReason>();
        if (invalidSourceEvidenceReferenceCount > 0)
            reasons.Add(new DecisionReason("case_evidence_integrity.invalid_source_evidence_reference",
                "Invalid source evidence reference", invalidSourceEvidenceReferenceCount, 0, "maximum", "critical"));
        if (brokenCanonicalEvidenceLineageCount > 0)
            reasons.Add(new DecisionReason("case_evidence_integrity.broken_canonical_evidence_lineage",
                "Broken canonical evidence lineage", brokenCanonicalEvidenceLineageCount, 0, "maximum", "critical"));

        return new CaseIntegrityAssessment(true, invalidSourceEvidenceReferenceCount, brokenCanonicalEvidenceLineageCount, reasons);
    }

    /// <summary>
    /// Applies a case-integrity assessment on top of an already-computed
    /// metric-based decision. Any case-integrity failure forces
    /// NOT_READY_FOR_DEPLOYMENT, no matter how well the agent's report scored
    /// on the judge metrics -- "a benchmark should not be considered
    /// assurance-valid if the underlying canonical case itself has unresolved
    /// evidence-integrity failures". A clean case (or no case at all) leaves
    /// the original decision completely untouched.
    /// </summary>
    public static AssuranceDecision ApplyCaseIntegrityGate(AssuranceDecision decision, CaseIntegrityAssessment caseIntegrity)
    {
        if (caseIntegrity.Reasons.Count == 0)
            return decision;

        return decision with
        {
            Overall = "NOT_READY_FOR_DEPLOYMENT",
            Reasons = decision.Reasons.Concat(caseIntegrity.Reasons).ToList(),
            Reason = decision.Overall == "NOT_READY_FOR_DEPLOYMENT"
                ? decision.Reason
                : $"The underlying canonical case has unresolved evidence-integrity failures ({string.Join("; ", caseIntegrity.Reasons.Select(r => r.Label))}), independent of the judge's metrics above ({decision.Reason})",
        };
    }
}

/// <summary>Case-level evidence-integrity result, translated into assurance-decision reasons.</summary>
public sealed record CaseIntegrityAssessment(
    bool Present,
    int InvalidSourceEvidenceReferenceCount,
    int BrokenCanonicalEvidenceLineageCount,
    IReadOnlyList<DecisionReason> Reasons);

/// <summary>
/// One threshold from an assurance policy (e.g. "EGHR &lt;= 5%").
/// Required=true (the default) means failing it blocks deployment
/// outright; Required=false means failing it only downgrades the decision
/// to PASS_WITH_CONDITIONS.
/// </summary>
public sealed record MetricThreshold(
    string Metric, string Label, string Direction, double Threshold, string Unit, bool Required = true);

/// <summary>A measured metric evaluated against its threshold, or NOT_EVALUATED if no value was available.</summary>
public sealed record MetricResult(string Metric, string Label, double? Value, string Unit, MetricThreshold? Threshold, string Status);

/// <summary>One structured reason a metric contributed to a non-PASS decision.</summary>
public sealed record DecisionReason(string Metric, string Label, double? Actual, double Threshold, string Rule, string Severity);

/// <summary>The overall assurance decision plus the structured reasoning behind it.</summary>
public sealed record AssuranceDecision(
    string Overall,
    IReadOnlyList<MetricResult> Results,
    int EvaluatedCount,
    int TotalDefinedCount,
    IReadOnlyList<string> NotEvaluatedDimensions,
    IReadOnlyList<DecisionReason> Reasons,
    string Reason);
