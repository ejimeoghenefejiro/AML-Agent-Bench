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

This claim-level representation is conceptually more important than global citation counting: two reports can have identical overall citation-precision numbers while one correctly grounds every individual claim and the other grounds claims to the wrong evidence entirely.

**Implemented:** `AmlAgent.Evidence.Claim` (`ClaimId`, `Text`, `Material`, `AgentEvidence`) and `ReferenceEvidence` (`Required`, `AcceptableAlternatives`, `Corroborating`) are the concrete `c_i` and `E_i*` this model describes. `AmlAgent.Evidence.ClaimLevelScoring` computes, per claim: whether it's supported (`IsSupported` — the agent's evidence is a superset of `Required`, or a superset of any one `AcceptableAlternatives` set — see the class's own doc comment for why this specific rule was chosen among several defensible options), and claim-level precision/recall (`Score`); across a claim set: macro-averaged precision/recall/F1 and Claim Support Coverage (`ComputeClaimLevelTraceability`, `ComputeClaimSupportCoverage`). `EvidenceTraceabilityProfileBuilder.Build` takes an optional `claims` parameter that, when supplied, populates `claim_support_coverage`, `claim_level_precision/recall/f1`, and a per-claim `claim_scores` array in `assurance_profile.json` — see `tests/AmlAgent.Tests/ClaimLevelScoringTests.cs` and `EvidenceTraceabilityProfileBuilderTests.cs`.

**Not yet implemented:** a live source of claim-level annotations. `judge_report.json`'s existing `claims` array (produced by the LLM judge for the EGHR check) only carries `{text, cited_txn_ids, support}` — no `claim_id`, `material` flag, or `reference_evidence` spec. No task's `evidence-annotations.json` has been re-annotated at claim level yet (see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md)). So `claim_support_coverage` is genuinely computable today, and is exercised by real tests, but stays `null` in every actual `assurance_profile.json` produced by a live run until (a) a task has real claim-level gold annotations and (b) the judge is extended to emit `claim_id`/`material`/per-claim `agent_evidence` — both explicit next steps, not silently assumed done.

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

### Evidence node, realised: `EvidenceReference`

The "evidence node" row above is no longer transaction-only in code. `AmlAgent.Evidence.EvidenceReference` (`EvidenceId`, `EvidenceType`, `Source`, plus optional `EntityId`/`RecordKey`) generalises what a citable evidence node can be — transaction, account, customer, entity, relationship, case, alert, evidence record, jurisdiction, or SAR, matching every canonical record type `AmlAgent.Adapters.Canonical.CanonicalAmlCase` carries. `CanonicalAmlCaseEvidenceExtensions.ToEvidenceReferences()` converts a merged multi-source case into the full evidence universe, and `EvidenceScoring.ComputeTraceability(string, IReadOnlyCollection<EvidenceReference>, IReadOnlyCollection<EvidenceReference>?)` scores a report against it — recognising a citation to a relationship id, SAR id, or watchlist entry for the first time, not just transaction IDs (see `tests/AmlAgent.Tests/EvidenceReferenceScoringTests.cs`).

This is now wired into the *live* judge, not just the library. `agents/csharp-sk/Agent/JudgeAgent.cs` detects a `case-definition.json` staged into the workspace (multi-source tasks only — every task that predates this feature has none) and, when present, reloads it independently via `CaseLoader`/`AdapterRegistry` to get the full merged `CanonicalAmlCase`, converts it with `ToEvidenceReferences()`, and scores traceability (and EGHR's citation-override check) against that whole evidence universe instead of the flat `grounding_inputs` transaction column. Task-006 and every other task without a `case-definition.json` still take the original `ComputeTraceability(string, IReadOnlySet<string>, IReadOnlySet<string>?)` path unchanged. Task-007 is the first task exercising the new path: its `evidence-annotations.json` now carries a generalised `gold_evidence_ids` field (relationship hops `R1`–`R6` and the `WATCHLIST1` corroboration, alongside the legacy transaction ids) so gold evidence for the network reconstruction is no longer transaction-only either. This was verified against the real task-007 case data (not just unit tests) — a synthetic report citing relationship and watchlist ids was scored via the real `CaseLoader` → `ToEvidenceReferences()` → `ComputeTraceability` pipeline and correctly recognised all of them as grounded while still flagging a shape-fabricated transaction id.

One thing this does **not** yet do: fabrication detection for non-transaction-shaped ids — an agent inventing a relationship id that was never real is not yet caught, since there is no single universal id shape to pattern-match across arbitrary evidence types (only the legacy transaction-id regex still catches shape-fabricated transaction citations). Claim-level annotation (per [Fix #2](#formal-claim-evidence-model)'s `Claim`/`ReferenceEvidence` model) has also not yet been authored for task-007 — its gold evidence is still report-level, only generalised beyond transaction ids.

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

**Two denominators, both reported, neither silent (fix #4).** `E_i^agent` in the formula above is *everything the agent cited* — it does not say "everything the agent validly cited". A report that cites a fabricated id alongside real ones is exactly the case where two readings of "precision" diverge, and the metric now computes both rather than picking one implicitly:

| Field (`evidence_traceability` / `TraceabilityResult`) | Denominator | Behaviour under fabrication |
|---|---|---|
| `precision` / `f1` (primary) | ALL distinct cited evidence, fabricated included | Degrades — a fabricated citation lowers precision, matching the formula above literally and the standard IR definition of precision |
| `valid_evidence_precision` / `valid_evidence_f1` | Grounded (real) citations only | Unaffected — fabrication is invisible to it by design, since the denominator excludes fabricated citations entirely |

Before this fix, only the second behaviour existed and was reported under the plain name `precision` — [validation/gold/traceability/04_fabricated_evidence_ids.json](../validation/gold/traceability/04_fabricated_evidence_ids.json) is the fixture that first surfaced this as worth resolving rather than leaving implicit. Both formulas are implemented, both are pinned by tests on both sides of the divergence (`tests/AmlAgent.Tests/EvidenceScoringTests.cs`, `tests/AmlAgent.ResearchValidation/TraceabilityValidationTests.cs`, `tests/AmlAgent.ResearchValidation/DiscriminationValidationTests.cs`), and neither is derivable from the other without also knowing `fabricated_citations`/`grounded_citations`. **`precision` is the metric to cite as the PhD's primary reported number** (it matches the framework's own formal EP definition and cannot be gamed by fabricating plausible-looking evidence); `valid_evidence_precision` exists for anyone who deliberately wants precision reported conditional on non-fabricated citations, and must always be read alongside `fabricated_citations`/`invalid_reference_count` (RVR), never alone, since a perfect `valid_evidence_precision` says nothing about whether the report also fabricated evidence.

One edge case worth being explicit about: a report citing *only* fabricated ids scores `precision = 0.0` (well-defined — it cited things, none were real or gold) but `valid_evidence_precision = null` (undefined — there is no grounded citation to compute a ratio over). `null` here means "no denominator", not "no evidence found"; do not read it as zero.

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

Distinct from evidence recall: recall asks "how much of the reference evidence did the agent cite anywhere"; coverage asks "how many of the agent's claims individually have adequate support". **Implemented** — `AmlAgent.Evidence.ClaimLevelScoring.ComputeClaimSupportCoverage`, wired into `EvidenceTraceabilityProfileBuilder.Build`'s optional `claims` parameter (see [Formal claim-evidence model](#formal-claim-evidence-model) above). What remains open is a live source of the claim-level material-claim identification and reference-evidence annotation this needs as input (see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md)) — the computation itself is done and tested.

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

**Fix #6 — correcting an overstatement:** `invalid_reference` and `evidence_omission` are fully deterministic (citation-existence check against the case's evidence ids; gold-evidence-set comparison — no LLM involved in either). `unsupported_claim` is **not** deterministic in the same way, and describing it as such overstates what the pipeline actually does: whether a claim is `supported`/`unsupported`/`contradicted` is an LLM self-label (`EvidenceScoring.ScoreClaims`'s `claim.Support`, produced by the judge model reading the claim against the grounding data). The *only* deterministic part is a narrow backstop — a claim citing a fabricated (nonexistent) evidence id is force-overridden to `unsupported` regardless of what the LLM said, so the judge cannot inflate its own grounding by mislabelling a fabricated citation as supported. Every other `unsupported_claim` (a claim whose citations all exist, but the LLM judged it unsupported anyway) is a semantic judgement call, not a deterministic computation — it belongs conceptually with `evidence_mismatch`/`insufficient_evidence`/`overcitation` below as LLM-originated, constrained by (not derived purely from) a deterministic check. `evidence_mismatch`, `insufficient_evidence`, and `overcitation` require semantic judgement (see [LLM-as-judge positioning](#llm-as-judge-positioning) below) and are only partially scored today — one exception: `insufficient_evidence` failures generated from claim-level `ReferenceEvidence` (`ClaimLevelScoring.IsSupported`, wired into `EvidenceTraceabilityProfileBuilder.Build`'s optional `claims` parameter) ARE fully deterministic set-comparison, no LLM involved, whenever claim-level annotations exist for a task — none do in a live run today (see [Formal claim-evidence model](#formal-claim-evidence-model)), so this exception is currently theoretical rather than exercised. `traceability_break` is assessed structurally by the case-integrity/evidence-reference-validation layer (`AmlAgent.Adapters.Canonical.EvidenceIntegrityValidator`) for the canonical-data side of the pipeline, and is a genuinely open question for the agent-output side (see `validation/gold/discrimination/*/04_incorrect_conclusion_plausible_explanation.json` for a concrete case where a report is perfectly traceable yet its conclusion is wrong — traceability and correctness are not the same property). `attribution_ambiguity` as a *detected failure* is **still not implemented** — nothing flags that an agent's report was itself ambiguous about which evidence set it relied on. What IS now implemented: the multiple-acceptable-evidence-set annotation (`ReferenceEvidence.AcceptableAlternatives`, see [Formal claim-evidence model](#formal-claim-evidence-model) above) means an agent citing a genuinely valid alternative set is no longer unfairly scored as if it omitted required evidence — `ClaimLevelScoring.IsSupported` credits either `Required` or any one `AcceptableAlternatives` set equally. What remains open is the reverse case this failure type actually names: the AGENT itself failing to clarify which of several plausible sets it's using, as opposed to the annotation now correctly allowing for more than one right answer.

## Legacy EGHR metric

The prototype's original primary metric, "Evidence-Grounded Hallucination Rate" (EGHR — `AmlAgent.Evidence.EvidenceScoring.ScoreClaims`), is **retained in code as a legacy/secondary metric, not deleted**, and is not described as a primary contribution of this PhD. It remains useful and is not silently removed because:

- it already implements a deterministic citation-existence backstop, which maps directly onto `invalid_reference` in the taxonomy above; that same backstop also *contributes to* `unsupported_claim` (any claim it force-overrides), but does not make `unsupported_claim` itself deterministic — most `unsupported_claim` labels come from the LLM judge's own support classification, not this backstop (see [Traceability failure taxonomy](#traceability-failure-taxonomy) above, fix #6);
- removing it would break existing tests and the existing assurance-profile output without adding measurement capability;
- `docs/research-scope-mapping.md` and `validation/gold/eghr/*.json` document its behaviour (including known definitional gaps, e.g. no distinct "partially supported" bucket) in detail already, and that validation work remains valid evidence about the deterministic backstop's correctness even though EGHR itself is no longer the headline construct.

Concretely: EGHR's `unsupported_count` and `contradicted_count` are the same signal as `unsupported_claim` and (for a citation that directly contradicts its own cited evidence) `evidence_mismatch`; EGHR's citation-existence override is the same signal as `invalid_reference`. Migration is staged, not a rename: EGHR fields stay live in `assurance_profile.json` and `judge_report.json`; new Evidence Traceability Profile fields are additive alongside them (see [docs/research-scope-mapping.md](research-scope-mapping.md) for the schema).

## Outcome correctness vs. task performance

**Fix #5.** H4 (`docs/experimental-design.md`) asks whether task performance is associated with evidence traceability — a question that requires the two variables to be measured independently. Before this fix, "task performance" meant the qualitative rubric's `overall_percentage`, and every task's rubric.json mixed outcome-correctness dimensions (did the agent identify the right victim/mule/exit accounts, typology, network path, and correctly clear innocent accounts) together with citation-quality dimensions (`evidence_grounding`, `avoids_unsupported_claims`, `evidence_traceability`, and similar). Since `evidence_traceability` is one of the rubric dimensions summed into `overall_percentage`, correlating "task performance" against the benchmark's deterministic evidence-traceability metric was partially correlating a variable against a component of itself — contaminating exactly the comparison H4 needs to make, and making any reported "discordant case" (high task performance, low traceability, or vice versa) partly definitionally impossible rather than a real empirical finding.

**Resolution:** each rubric dimension in `tasks/<id>/rubric.json` now carries an optional `"category"` field — `outcome_correctness`, `evidence_quality`, or `process_quality` — and `AmlAgent.Evidence.RubricCategoryScoring.ComputeCategoryTotals` (wired into `JudgeAgent.cs`) aggregates per-category subtotals into `judge_report.json`'s new `rubric_by_category` object, with a convenience top-level `outcome_correctness` alias for the specific field H4 needs. Task-007's rubric also splits `explanation_quality` (writing quality/tone — `process_quality`) from a new `typology_identification` dimension (`outcome_correctness`), since typology-naming correctness and writing-quality assessment are conceptually distinct and the original combined dimension made it impossible to separate them.

| Category | Task-007 dimensions | Task-006 dimensions |
|---|---|---|
| `outcome_correctness` | `network_identification`, `avoids_false_implication`, `typology_identification` | `temporal_reasoning`, `anomaly_detection` |
| `evidence_quality` | `evidence_grounding`, `avoids_unsupported_claims`, `evidence_traceability` | `evidence_citation`, `avoids_unsupported_claims` |
| `process_quality` | `explanation_quality`, `audit_trail_awareness` | `fact_vs_assumption`, `compliance_tone` |

`overall_score`/`overall_percentage`/`verdict` are **unchanged in meaning** — they remain the full-rubric holistic "is this report good enough to ship" gate, and existing PASS/FAIL behaviour, thresholds, and downstream consumers of that field are untouched. `outcome_correctness` is a new, additive, construct-clean measurement, not a replacement. In `assurance_profile.json`, `task_performance_percentage` (still the full rubric) and the new `outcome_correctness_percentage` are both exposed as separate policy metrics (`assurance/policy.default.json`, `assurance/policies/bank-strict.json`), with the former's policy label updated to stop calling it a "proxy for detection performance" — that framing is now `outcome_correctness_percentage`'s job. **`outcome_correctness_percentage` is the field H4's analysis should correlate against `evidence_traceability_f1`/`precision`/`recall`, not `task_performance_percentage`.**

A rubric with no `outcome_correctness`-tagged dimensions (any rubric written before this fix) yields `outcome_correctness: null` in `judge_report.json` and `outcome_correctness_percentage: null` in the assurance profile — "not measured", never fabricated as zero. Verified against both tasks' real `rubric.json` files (not just synthetic fixtures): `tests/AmlAgent.Tests/RubricCategoryScoringTests.cs` unit-tests the aggregation arithmetic in isolation, and a manual end-to-end check against the live `tasks/task-006-.../rubric.json` and `tasks/task-007-.../rubric.json` files confirmed every dimension in both resolves to exactly one of the three categories with no gaps.

## LLM-as-judge positioning

The LLM judge is not the ground-truth evaluator. Deterministic checks are preferred wherever possible:

**Deterministic today:** whether a transaction ID exists; whether a cited ID belongs to the allowed case; set overlap with curated evidence; reference validity rate; precision/recall/F1 arithmetic; provenance hashes; schema validation.

**Requires semantic judgement (LLM, eventually validated against humans):** whether a valid record actually supports a natural-language claim; whether evidence is sufficient for the strength of the conclusion; whether two differently-worded claims are materially equivalent. Any LLM-based semantic evaluator here must eventually be validated against human annotation (see [docs/validation-plan.md](validation-plan.md#convergent-validity)) before its output can be treated as more than a provisional signal. Judge repeatability itself is measured, not assumed — see `validation/experiments/README.md` item 7 and `src/AmlAgent.Harness/ExperimentJudgeRepeatCommand.cs`.
