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
/// Fields the current implementation cannot yet compute (claim_support_coverage,
/// evidence_sufficiency_rate, reconstruction_success) are explicitly null, never
/// fabricated as zero. run_reproducibility_note is a fixed descriptive string,
/// not a computed rate -- see docs/evidence-traceability-framework.md#run-reproducibility
/// for why "reproducibility" here is an experimental property (repeated-run
/// variance), not something a single profile can report a number for.
/// </summary>
public static class EvidenceTraceabilityProfileBuilder
{
    public static JsonObject Build(JsonObject? eghr, JsonObject? evidenceTraceability)
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

        return new JsonObject
        {
            ["reference_validity_rate"] = referenceValidityRate,
            ["evidence_precision"] = evidenceTraceability?["precision"]?.DeepClone(),
            ["evidence_recall"] = evidenceTraceability?["recall"]?.DeepClone(),
            ["evidence_traceability_f1"] = evidenceTraceability?["f1"]?.DeepClone(),
            ["claim_support_coverage"] = null, // not yet implemented -- requires claim-level material-claim identification
            ["evidence_sufficiency_rate"] = null, // not yet implemented -- requires validated sufficiency annotation
            ["reconstruction_success"] = null, // not yet implemented -- no per-claim reconstruction check exists for agent output yet
            ["run_reproducibility_note"] = "Deterministic scoring (this profile, EGHR, traceability, policy evaluation) is exactly repeatable given identical inputs -- see AmlAgent.ResearchValidation.DeterminismTests. The underlying LLM's own output is not deterministic; use `aml-harness experiment repeat`/`experiment judge-repeat` to measure that separately, not this field.",
            ["invalid_reference_count"] = fabricatedCount,
            ["unsupported_claim_count"] = (int?)eghr?["unsupported_count"],
            ["traceability_failures"] = failures,
        };
    }
}
