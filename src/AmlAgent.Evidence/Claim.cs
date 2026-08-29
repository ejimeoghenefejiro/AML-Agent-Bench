namespace AmlAgent.Evidence;

/// <summary>
/// One material claim in an agent's report, with its own evidence and
/// reference-evidence structure -- the claim-level unit the formal
/// claim-evidence model (docs/evidence-traceability-framework.md#formal-claim-evidence-model)
/// describes, now implemented rather than only documented. Generalises
/// AmlAgent.Evidence.ClaimInput (which carries only a flat CitedTxnIds list
/// for the EGHR check) to the full claim-evidence-graph picture: what the
/// agent actually cited (AgentEvidence) is compared against a structured
/// reference-evidence spec (ReferenceEvidence) that can express multiple
/// equally-valid ways to support the claim, not just one flat gold set.
///
/// AgentEvidence holds evidence ids (any type -- see EvidenceReference), not
/// EvidenceReference objects themselves: a claim cites evidence by id, the
/// same way a report cites it in text.
/// </summary>
public sealed record Claim(
    string ClaimId,
    string Text,
    bool Material,
    IReadOnlyList<string> AgentEvidence,
    ReferenceEvidence? ReferenceEvidence = null);

/// <summary>
/// The validated reference evidence for one claim (see
/// docs/evidence-annotation-protocol.md#multiple-valid-gold-handling).
///
/// - Required: evidence that must ALL be cited for the claim to count as
///   supported via the "default" path.
/// - AcceptableAlternatives: other complete evidence sets that are equally
///   valid INSTEAD of Required -- citing all of Required OR all of any one
///   alternative set is sufficient; a claim is not required to satisfy both.
/// - Corroborating: evidence that strengthens the claim but is never
///   required for it to count as supported.
///
/// Null (the default when a claim hasn't been annotated at this level yet)
/// means "no reference evidence to check against" -- ClaimLevelScoring
/// treats this as un-scorable, not as automatically supported or
/// unsupported, and excludes it from Claim Support Coverage's denominator.
/// </summary>
public sealed record ReferenceEvidence(
    IReadOnlyList<string> Required,
    IReadOnlyList<IReadOnlyList<string>>? AcceptableAlternatives = null,
    IReadOnlyList<string>? Corroborating = null);
