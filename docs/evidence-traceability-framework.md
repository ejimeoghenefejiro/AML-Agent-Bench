# Evidence Traceability Framework

> The formal definition, claim–evidence model, measurement model, and failure
> taxonomy underlying AML-Agent-Bench's sole primary doctoral construct. See
> [docs/abstract.md](abstract.md) for the one-paragraph summary and
> [docs/research-scope-mapping.md](research-scope-mapping.md) for what of this
> framework is implemented today versus planned.

## Definition

> **Evidence traceability is the degree to which material claims produced by an autonomous AML agent can be systematically linked to identifiable, valid, relevant, and sufficient evidence within the underlying investigation record, such that the evidentiary basis of the conclusion can be independently reconstructed and reviewed.**

The framework distinguishes five properties a claim–evidence link can have:

| Property | Meaning |
|---|---|
| **Identifiable** | The referenced evidence can be uniquely located — by transaction ID, record ID, entity ID, case ID, timestamp, or another stable key. |
| **Valid** | The referenced evidence actually exists in the source case data. |
| **Relevant** | The evidence is pertinent to the claim for which it is cited. |
| **Sufficient** | The evidence is adequate to support the strength and scope of the claim. A valid, relevant citation is not automatically sufficient evidence — e.g. one transaction is rarely sufficient to establish "layering". |
| **Reconstructable** | An independent evaluator can recover the claim-to-evidence mapping from the benchmark artefacts and reproduce the evaluation. |

## Formal claim-evidence model

Let the set of material claims produced by an agent for one investigative output be:

```
C = { c_1, c_2, ..., c_n }
```

Let the available evidence items in the case be:

```
E = { e_1, e_2, ..., e_m }
```

Define a traceability relation `T ⊆ C × E`, where `(c_i, e_j) ∈ T` means evidence item `e_j` supports material claim `c_i`.

For each claim `c_i`, define a **validated reference evidence set** `E_i*` — the evidence a human-adjudicated gold annotation says is required, acceptable, or corroborating for that claim (see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md)). The benchmark compares the agent-produced evidence set for that claim, `E_i^agent`, against `E_i*`.

This claim-level representation is conceptually more important than global citation counting: two reports can have identical overall citation-precision numbers while one correctly grounds every individual claim and the other grounds claims to the wrong evidence entirely. Claim-level analysis is on the roadmap (see [docs/research-scope-mapping.md](research-scope-mapping.md)) but not yet fully implemented — the current codebase computes traceability at the report level (`AmlAgent.Evidence.EvidenceScoring.ComputeTraceability`), not yet the per-claim `E_i^agent` vs `E_i*` comparison this model describes. Where that gap matters, it is stated explicitly rather than implied to already work.

### Claim-evidence graph representation

An agent's output can be modelled as a bipartite claim-evidence graph rather than a flat citation list. This is a richer representation than simple citation counting because it supports analyses citation counts cannot express: missing edges, irrelevant edges, incomplete evidence sets, redundant evidence, and broken multi-hop evidence chains.

| Graph element | Interpretation |
|---|---|
| Claim node | A material factual or analytical assertion made by the AML agent. |
| Evidence node | A transaction, account record, temporal observation, rule, graph edge, document, or other case artefact. |
| Support edge | The asserted relationship that a specific evidence item supports a specific claim (an agent-produced `(c_i, e_j) ∈ T`). |
| Gold edge | An independently annotated reference claim-evidence relationship (`(c_i, e_j)` where `e_j ∈ E_i*`). |
| Missing edge | Material evidence required by the gold standard but omitted by the agent. |
| Invalid edge | A citation to evidence that does not exist or cannot be resolved. |
| Mismatch edge | Evidence exists but does not support the claim to which it is linked. |

This graph model is the conceptual target the [claim-level schema](research-scope-mapping.md#planned-claim-level-schema) is meant to realise; the currently-implemented report-level scoring is a projection of this graph (aggregate edge counts), not the graph itself.

## Measurement model: the Evidence Traceability Profile

The benchmark does not treat any single number as the whole construct. It reports an **Evidence Traceability Profile** with the following components. Fields not yet implemented are reported as `null` (or an explicit `not_implemented` status), never fabricated as zero.

### Reference Validity Rate (RVR / "Citation Validity")

Whether evidence references actually exist in the underlying case data.

```
RVR = valid evidence references / all evidence references
```

The existing "fabricated citation count" is retained as a complementary raw failure count alongside RVR. Implemented as `reference_validity_rate` (`AmlAgent.Evidence.EvidenceTraceabilityProfileBuilder`) — a naming note, not two different metrics.

### Evidence Precision (EP)

At claim level: `EP_i = |E_i^agent ∩ E_i*| / |E_i^agent|`. Report-level (micro) precision, the currently-implemented form, aggregates over all cited evidence in the report rather than per claim — the two are conceptually distinct and both are useful; they are labelled separately, never conflated.

### Evidence Recall (ER)

At claim level: `ER_i = |E_i^agent ∩ E_i*| / |E_i*|`. Same micro/macro distinction as precision.

### Evidence Traceability F1 (ETF1)

```
ETF1 = 2 × (EP × ER) / (EP + ER)
```

Retained as a metric; not presented as the sole novelty of this PhD — see [docs/research-problem.md](research-problem.md) for why the contribution is the measurement *framework*, not this formula.

### Claim Support Coverage (CSC)

```
CSC = material claims with adequate supporting evidence / all material claims requiring evidence
```

Distinct from evidence recall: recall asks "how much of the reference evidence did the agent cite anywhere"; coverage asks "how many of the agent's claims individually have adequate support". **Not yet implemented** — requires claim-level material-claim identification, which is itself an open annotation-methodology question (see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md)).

### Evidence Sufficiency Rate (ESR)

```
ESR = claims with sufficient supporting evidence / claims requiring evidence
```

A transaction may be valid and relevant yet insufficient to establish a multi-transaction conclusion such as layering, circularity, rapid movement, or temporal escalation. **Not yet implemented** — sufficiency judgements are inherently semantic and require validated annotation before they can be scored, deterministically or otherwise.

### Reconstruction Success (RS)

```
RS = reconstructable claim-evidence chains / required chains
```

Whether an independent evaluator can rebuild the evidence chain for a claim from the benchmark artefacts alone — the deterministic-plus-procedural counterpart to the `traceability_break` failure class below. **Not yet implemented as a scored rate.** The structural half already exists for canonical case data (`AmlAgent.Adapters.Canonical.EvidenceIntegrityValidator`, which detects when a reference cannot be resolved at all), but nothing yet turns that into a per-claim RS score for agent output specifically.

### Run Reproducibility

Whether scores are stable given identical artefacts and configuration — an experimental property (repeatability/variance across controlled reruns), not a single computed rate. The infrastructure to measure this exists and has produced real, live data (`aml-harness experiment repeat` / `experiment judge-repeat`, demonstrated in `docs/preliminary-results.md`'s repeated-run data point), but only as a 2-run proof of concept — see [docs/validation-plan.md](validation-plan.md#reliability) for what a statistically meaningful reading would require. `assurance_profile.json`'s `reproducibility_note` field already states plainly what *is* deterministic (evidence scoring, traceability, policy evaluation) versus what isn't (the underlying LLM's own output).

### Reproducibility / provenance indicators

Retained and already implemented, not treated as a separate doctoral construct: dataset hash, task fingerprint, rubric/evidence-annotation hash, git commit SHA, agent identifier/version, model identifier, runtime configuration, benchmark version, result hash. See `assurance/README.md` for where these are emitted today.

A composite single score across every component above is deliberately not introduced yet: premature aggregation could hide operationally important failure patterns that only show up when the components are read separately (see the discriminant-validity discussion in [docs/validation-plan.md](validation-plan.md#discriminant-validity)).

## Traceability failure taxonomy

Concrete failure modes a claim–evidence link can exhibit, replacing "hallucination" as the organising vocabulary (see [Legacy: the EGHR metric](#legacy-eghr-metric) below for how the old metric maps onto this taxonomy):

| Failure type | Definition |
|---|---|
| `invalid_reference` | Agent cites an evidence identifier that does not exist in the case data. |
| `unsupported_claim` | A material claim has no identifiable supporting evidence. |
| `evidence_mismatch` | Evidence exists but is not relevant to the claim it is used to support. |
| `evidence_omission` | Material reference evidence is not recovered/cited by the agent. |
| `insufficient_evidence` | The cited evidence is relevant but inadequate to support the scope/strength of the claim. |
| `overcitation` | The agent cites excessive irrelevant evidence, reducing precision. |
| `traceability_break` | The evidence chain cannot be independently reconstructed from benchmark artefacts. |
| `attribution_ambiguity` | More than one plausible evidence set could support the claim and the agent does not clarify which one it relies on. |

`invalid_reference`, `unsupported_claim`, and `evidence_omission` are deterministically detectable today from citation-existence checks and gold-evidence-set comparison. `evidence_mismatch`, `insufficient_evidence`, and `overcitation` require semantic judgement (see [LLM-as-judge positioning](#llm-as-judge-positioning) below) and are only partially scored today. `traceability_break` is assessed structurally by the case-integrity/evidence-reference-validation layer (`AmlAgent.Adapters.Canonical.EvidenceIntegrityValidator`) for the canonical-data side of the pipeline, and is a genuinely open question for the agent-output side (see `validation/gold/discrimination/*/04_incorrect_conclusion_plausible_explanation.json` for a concrete case where a report is perfectly traceable yet its conclusion is wrong — traceability and correctness are not the same property). `attribution_ambiguity` is **not detected at all today** — it requires the multiple-acceptable-evidence-set annotation (`acceptable_alternatives`) sketched in the [planned claim-level schema](research-scope-mapping.md#planned-claim-level-schema), which doesn't exist yet; today's flat gold-evidence lists cannot represent "either of these two evidence sets is acceptable," so an agent citing a genuinely valid alternative set is currently indistinguishable from one omitting required evidence.

## Legacy EGHR metric

The prototype's original primary metric, "Evidence-Grounded Hallucination Rate" (EGHR — `AmlAgent.Evidence.EvidenceScoring.ScoreClaims`), is **retained in code as a legacy/secondary metric, not deleted**, and is not described as a primary contribution of this PhD. It remains useful and is not silently removed because:

- it already implements a deterministic citation-existence backstop that maps directly onto `invalid_reference` and `unsupported_claim` in the taxonomy above;
- removing it would break existing tests and the existing assurance-profile output without adding measurement capability;
- `docs/research-scope-mapping.md` and `validation/gold/eghr/*.json` document its behaviour (including known definitional gaps, e.g. no distinct "partially supported" bucket) in detail already, and that validation work remains valid evidence about the deterministic backstop's correctness even though EGHR itself is no longer the headline construct.

Concretely: EGHR's `unsupported_count` and `contradicted_count` are the same signal as `unsupported_claim` and (for a citation that directly contradicts its own cited evidence) `evidence_mismatch`; EGHR's citation-existence override is the same signal as `invalid_reference`. Migration is staged, not a rename: EGHR fields stay live in `assurance_profile.json` and `judge_report.json`; new Evidence Traceability Profile fields are additive alongside them (see [docs/research-scope-mapping.md](research-scope-mapping.md) for the schema).

## LLM-as-judge positioning

The LLM judge is not the ground-truth evaluator. Deterministic checks are preferred wherever possible:

**Deterministic today:** whether a transaction ID exists; whether a cited ID belongs to the allowed case; set overlap with curated evidence; reference validity rate; precision/recall/F1 arithmetic; provenance hashes; schema validation.

**Requires semantic judgement (LLM, eventually validated against humans):** whether a valid record actually supports a natural-language claim; whether evidence is sufficient for the strength of the conclusion; whether two differently-worded claims are materially equivalent. Any LLM-based semantic evaluator here must eventually be validated against human annotation (see [docs/validation-plan.md](validation-plan.md#convergent-validity)) before its output can be treated as more than a provisional signal. Judge repeatability itself is measured, not assumed — see `validation/experiments/README.md` item 7 and `src/AmlAgent.Harness/ExperimentJudgeRepeatCommand.cs`.
