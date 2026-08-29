namespace AmlAgent.Evidence;

/// <summary>
/// Claim-level scoring over the Claim/ReferenceEvidence model: whether a
/// claim's cited evidence is adequate (IsSupported), claim-level precision/
/// recall/F1 (macro-averaged across claims -- the counterpart to
/// EvidenceScoring's report-level/micro precision/recall), and Claim Support
/// Coverage (docs/evidence-traceability-framework.md#claim-support-coverage-csc).
///
/// The support/sufficiency rule below is a documented design choice, not a
/// value pulled from a single canonical source -- the annotation protocol
/// itself (docs/evidence-annotation-protocol.md#annotation-decisions) leaves
/// "are multiple evidence sets equally defensible" as an open annotation
/// question. The rule adopted here: a claim is supported if its agent
/// evidence is a superset of Required, OR a superset of any one
/// AcceptableAlternatives set -- Required and each alternative are each a
/// complete, independently-sufficient way to support the claim, not
/// components that must all be satisfied together.
/// </summary>
public static class ClaimLevelScoring
{
    /// <summary>
    /// Whether the claim's agent-cited evidence satisfies its reference
    /// evidence spec. A claim with no ReferenceEvidence (not annotated at
    /// this level yet) is vacuously supported when called directly -- there
    /// is nothing to check -- but such claims are excluded from Claim
    /// Support Coverage's denominator, so this vacuous-true value is never
    /// silently counted as a real "supported" result in an aggregate metric.
    /// </summary>
    public static bool IsSupported(Claim claim)
    {
        var reference = claim.ReferenceEvidence;
        if (reference is null) return true;

        var agentSet = new HashSet<string>(claim.AgentEvidence, StringComparer.OrdinalIgnoreCase);
        if (reference.Required.Count == 0 || reference.Required.All(agentSet.Contains))
            return true;

        return reference.AcceptableAlternatives?.Any(alt => alt.Count > 0 && alt.All(agentSet.Contains)) ?? false;
    }

    /// <summary>
    /// The resolved reference-evidence set used for this claim's precision/
    /// recall arithmetic: Required ∪ Corroborating, plus (if the agent's
    /// evidence fully matches one) the specific AcceptableAlternatives set
    /// the agent actually used -- so an agent that correctly cited a valid
    /// alternative instead of Required isn't penalised on recall against a
    /// set it was never expected to reproduce.
    /// </summary>
    public static IReadOnlySet<string> ResolveReferenceSet(Claim claim)
    {
        var reference = claim.ReferenceEvidence;
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reference is null) return resolved;

        resolved.UnionWith(reference.Required);
        if (reference.Corroborating is not null)
            resolved.UnionWith(reference.Corroborating);

        var agentSet = new HashSet<string>(claim.AgentEvidence, StringComparer.OrdinalIgnoreCase);
        var matchedAlternative = reference.AcceptableAlternatives?
            .FirstOrDefault(alt => alt.Count > 0 && alt.All(agentSet.Contains));
        if (matchedAlternative is not null)
            resolved.UnionWith(matchedAlternative);

        return resolved;
    }

    /// <summary>Precision/recall/support for one claim. Null when the claim isn't scorable (no ReferenceEvidence).</summary>
    public static ClaimScore Score(Claim claim)
    {
        if (claim.ReferenceEvidence is null)
            return new ClaimScore(claim.ClaimId, Supported: null, Precision: null, Recall: null, ReferenceSetSize: 0, AgentEvidenceSize: claim.AgentEvidence.Count, MatchedCount: 0);

        var agentSet = new HashSet<string>(claim.AgentEvidence, StringComparer.OrdinalIgnoreCase);
        var referenceSet = ResolveReferenceSet(claim);
        var matched = agentSet.Count(referenceSet.Contains);

        double? precision = agentSet.Count == 0 ? null : Math.Round((double)matched / agentSet.Count, 4);
        double? recall = referenceSet.Count == 0 ? null : Math.Round((double)matched / referenceSet.Count, 4);

        return new ClaimScore(claim.ClaimId, IsSupported(claim), precision, recall, referenceSet.Count, agentSet.Count, matched);
    }

    /// <summary>
    /// Claim Support Coverage: the proportion of material, annotated claims
    /// (Material == true AND ReferenceEvidence is not null) whose evidence is
    /// adequate. Claims without a reference-evidence annotation are excluded
    /// from the denominator entirely -- a partially-annotated claim set
    /// produces a coverage rate over the annotated subset, never a
    /// fabricated rate over claims that were never actually checked. Null
    /// when there are no scorable claims at all (not zero -- 0/0 is
    /// undefined, not "no claims supported").
    /// </summary>
    public static double? ComputeClaimSupportCoverage(IReadOnlyList<Claim> claims)
    {
        var scorable = claims.Where(c => c.Material && c.ReferenceEvidence is not null).ToList();
        if (scorable.Count == 0) return null;
        return Math.Round((double)scorable.Count(IsSupported) / scorable.Count, 4);
    }

    /// <summary>
    /// Macro-averaged claim-level precision/recall/F1 across scorable
    /// material claims, plus Claim Support Coverage -- the claim-level
    /// counterpart to EvidenceScoring.ComputeTraceability's report-level
    /// (micro) precision/recall. The two are conceptually distinct and both
    /// useful (see docs/evidence-traceability-framework.md); this method
    /// never overwrites or is conflated with the report-level result.
    /// </summary>
    public static ClaimLevelTraceabilityResult ComputeClaimLevelTraceability(IReadOnlyList<Claim> claims)
    {
        var scores = claims
            .Where(c => c.Material && c.ReferenceEvidence is not null)
            .Select(Score)
            .ToList();

        var macroPrecision = Average(scores.Select(s => s.Precision));
        var macroRecall = Average(scores.Select(s => s.Recall));
        double? f1 = macroPrecision is double p && macroRecall is double r && (p + r) > 0
            ? Math.Round(2 * p * r / (p + r), 4)
            : null;

        return new ClaimLevelTraceabilityResult(scores, macroPrecision, macroRecall, f1, ComputeClaimSupportCoverage(claims));
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var present = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : Math.Round(present.Average(), 4);
    }
}

/// <summary>One claim's precision/recall/support result. Supported/Precision/Recall are all null together when the claim has no ReferenceEvidence to score against.</summary>
public sealed record ClaimScore(
    string ClaimId,
    bool? Supported,
    double? Precision,
    double? Recall,
    int ReferenceSetSize,
    int AgentEvidenceSize,
    int MatchedCount);

/// <summary>Macro (claim-level) traceability result across a set of claims, plus Claim Support Coverage.</summary>
public sealed record ClaimLevelTraceabilityResult(
    IReadOnlyList<ClaimScore> ClaimScores,
    double? MacroPrecision,
    double? MacroRecall,
    double? MacroF1,
    double? ClaimSupportCoverage);
