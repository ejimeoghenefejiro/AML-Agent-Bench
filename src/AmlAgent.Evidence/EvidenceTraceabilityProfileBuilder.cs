using System.Text.Json.Nodes;

namespace AmlAgent.Evidence;

/// <summary>
/// Builds the additive Evidence Traceability Profile block for
/// assurance_profile.json (see docs/evidence-traceability-framework.md and
/// docs/research-scope-mapping.md#planned-claim-level-schema). Additive and
/// backward-compatible: it derives its fields from the EXISTING judge_report.json
/// "eghr"/"evidence_traceability"/"claims" objects that AmlAgent.Evidence.EvidenceScoring
/// already computes -- nothing here changes what those existing fields mean or
/// removes them; this is a second, differently-organised view over the same
/// underlying measurements, reorganised around the traceability failure taxonomy
/// instead of the legacy EGHR supported/unsupported/contradicted buckets.
///
/// Fields the current implementation cannot yet compute without claim-level
/// input (claim_support_coverage, claim_level_precision/recall/f1) or at all
/// (evidence_sufficiency_rate, reconstruction_success) are explicitly null,
/// never fabricated as zero. run_reproducibility_note is a fixed descriptive
/// string, not a computed rate -- see
/// docs/evidence-traceability-framework.md#run-reproducibility for why
/// "reproducibility" here is an experimental property (repeated-run
/// variance), not something a single profile can report a number for.
///
/// The optional `claims` parameter is the claim-level model
/// (docs/evidence-traceability-framework.md#formal-claim-evidence-model,
/// AmlAgent.Evidence.Claim/ReferenceEvidence/ClaimLevelScoring). No current
/// live caller passes it -- judge_report.json's "claims" array today only
/// carries {text, cited_txn_ids, support} for the EGHR check, not
/// {claim_id, material, reference_evidence} -- so in the live path this
/// stays null and every claim-level field stays null exactly as before this
/// parameter was added. It exists so claim-level scoring is ready to use the
/// moment a real source of claim-level annotations exists, without another
/// schema change.
/// </summary>
public static class EvidenceTraceabilityProfileBuilder
{
    public static JsonObject Build(JsonObject? eghr, JsonObject? evidenceTraceability, IReadOnlyList<Claim>? claims = null)
    {
        var citedDistinct = (int?)evidenceTraceability?["cited_txn_ids_distinct"];
        var fabricated = evidenceTraceability?["fabricated_citations"]?.AsArray();
        var fabricatedCount = fabricated?.Count ?? 0;

        double? referenceValidityRate = citedDistinct is int cd && cd > 0
            ? Math.Round((double)(cd - fabricatedCount) / cd, 4)
            : null;

        var failures = new JsonArray();

        foreach (var id in fabricated ?? new JsonArray())
        {
            failures.Add(new JsonObject
            {
                ["failure_type"] = "invalid_reference",
                ["claim_id"] = null,
                ["evidence_id"] = (string?)id,
                ["description"] = $"cited evidence id '{(string?)id}' does not exist in the case",
            });
        }

        foreach (var id in evidenceTraceability?["missing_gold_citations_list"]?.AsArray() ?? new JsonArray())
        {
            failures.Add(new JsonObject
            {
                ["failure_type"] = "evidence_omission",
                ["claim_id"] = null,
                ["evidence_id"] = (string?)id,
                ["description"] = $"gold-relevant evidence '{(string?)id}' was not cited",
            });
        }

        // NOT deterministic in the way invalid_reference/evidence_omission
        // above are (fix #6): claim["support"] originates from the LLM
        // judge's own self-labelling of each claim as supported/unsupported/
        // contradicted (see EvidenceScoring.ScoreClaims). The ONLY
        // deterministic part of this value is a narrow backstop -- a claim
        // citing a fabricated (nonexistent) evidence id is force-overridden
        // to "unsupported" regardless of what the LLM said. Every other
        // "unsupported" here (a claim whose citations all exist but the LLM
        // judged unsupported anyway) reflects an LLM judgement call, not a
        // deterministic computation. Do not describe unsupported_claim
        // failures as deterministically detected without that qualification.
        foreach (var claim in eghr?["claims"]?.AsArray() ?? new JsonArray())
        {
            if ((string?)claim?["support"] == "unsupported")
            {
                failures.Add(new JsonObject
                {
                    ["failure_type"] = "unsupported_claim",
                    ["claim_id"] = null,
                    ["evidence_id"] = null,
                    ["description"] = $"claim '{(string?)claim?["text"]}' has no identifiable supporting evidence",
                });
            }
        }

        ClaimLevelTraceabilityResult? claimLevel = claims is not null ? ClaimLevelScoring.ComputeClaimLevelTraceability(claims) : null;

        if (claimLevel is not null)
        {
            foreach (var score in claimLevel.ClaimScores.Where(s => s.Supported == false))
            {
                failures.Add(new JsonObject
                {
                    ["failure_type"] = "insufficient_evidence",
                    ["claim_id"] = score.ClaimId,
                    ["evidence_id"] = null,
                    ["description"] = $"claim '{score.ClaimId}' cites evidence but not enough to meet its reference-evidence spec",
                });
            }
        }

        return new JsonObject
        {
            ["reference_validity_rate"] = referenceValidityRate,
            ["evidence_precision"] = evidenceTraceability?["precision"]?.DeepClone(),
            ["evidence_recall"] = evidenceTraceability?["recall"]?.DeepClone(),
            ["evidence_traceability_f1"] = evidenceTraceability?["f1"]?.DeepClone(),
            // Fix #4: evidence_precision/evidence_traceability_f1 above use the
            // standard-IR definition (fabricated citations count against the
            // denominator). These two preserve the metric's original formula
            // (matched over real/grounded citations only) under an explicit
            // name -- see docs/evidence-traceability-framework.md#evidence-precision-ep.
            ["valid_evidence_precision"] = evidenceTraceability?["valid_evidence_precision"]?.DeepClone(),
            ["valid_evidence_f1"] = evidenceTraceability?["valid_evidence_f1"]?.DeepClone(),
            ["claim_support_coverage"] = claimLevel?.ClaimSupportCoverage, // report-level micro precision/recall above; claim-level (macro) below -- see docs/evidence-traceability-framework.md
            ["claim_level_precision"] = claimLevel?.MacroPrecision,
            ["claim_level_recall"] = claimLevel?.MacroRecall,
            ["claim_level_f1"] = claimLevel?.MacroF1,
            ["claim_scores"] = claimLevel is null ? null : new JsonArray(claimLevel.ClaimScores.Select(s => (JsonNode)new JsonObject
            {
                ["claim_id"] = s.ClaimId,
                ["supported"] = s.Supported,
                ["precision"] = s.Precision,
                ["recall"] = s.Recall,
            }).ToArray()),
            ["evidence_sufficiency_rate"] = null, // not yet implemented -- requires validated sufficiency annotation
            ["reconstruction_success"] = null, // not yet implemented -- no per-claim reconstruction check exists for agent output yet
            ["run_reproducibility_note"] = "Deterministic scoring (this profile, EGHR, traceability, policy evaluation) is exactly repeatable given identical inputs -- see AmlAgent.ResearchValidation.DeterminismTests. The underlying LLM's own output is not deterministic; use `aml-harness experiment repeat`/`experiment judge-repeat` to measure that separately, not this field.",
            ["invalid_reference_count"] = fabricatedCount,
            ["unsupported_claim_count"] = (int?)eghr?["unsupported_count"],
            ["traceability_failures"] = failures,
        };
    }
}
