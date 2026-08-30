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

**Live for task-007 since fix #7** (this section previously said "not yet implemented" — corrected as part of fix #10's documentation cleanup): a live source of claim-level annotations now exists. `task-007`'s `evidence-annotations.json` has six task-authored `material_claims` (`claim_id`, `text`, `required`/`acceptable_alternatives` reference evidence). `JudgeAgent.cs` loads them, asks the judge only to identify which evidence ids the report cites per claim (not whether the claim is material or what counts as adequate support — both authored/computed separately), and writes the result to `judge_report.json`'s `material_claims` array (distinct from the older, still-unchanged `claims` array the EGHR check uses, which still only carries `{text, cited_txn_ids, support}`). `AssuranceProfileBuilder.cs` reads `material_claims` back and passes it to `EvidenceTraceabilityProfileBuilder.Build`, populating `claim_support_coverage`/`claim_level_precision/recall/f1` in real `assurance_profile.json` output for task-007 — not just in tests. Task-006 has no `material_claims` annotation yet, so these fields stay `null` there. See [Claim Support Coverage (CSC)](#claim-support-coverage-csc) below for the full account.

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

Claim-level annotation for task-007's `material_claims` now exists too (fix #7) — see [Claim Support Coverage (CSC)](#claim-support-coverage-csc) below.

### Generic fabricated-evidence detection

**v0.3 validation-priorities fix #3, closing a gap this section used to describe as still open.** Recognising a *real* citation to any evidence type (above) is only half the fabrication-detection story — the other half is recognising a *fake* one. Before this fix, only the legacy transaction-id regex (`\bT[123]-\d{3}\b`) could catch a shape-plausible-but-nonexistent citation; an agent inventing a relationship id like `"R99"` when the case only has real `R1`–`R6` went completely undetected, since `R99` isn't a known id (so the exact-match pass ignores it) and doesn't look like a transaction id (so the legacy regex ignores it too).

`AmlAgent.Evidence.EvidenceScoring.InferEvidenceIdShapes` closes this generically, without hardcoding per-evidence-type patterns: for each real id in the case, it replaces every digit run with a placeholder and escapes the rest, so `"R1"`/`"R2"`/`"R6"` all produce the shape `R\d+`, `"WATCHLIST1"` produces `WATCHLIST\d+`, `"SAR-2026-001"` produces `SAR-\d+-\d+`, and so on — a shape *derived from the case's own real ids*, not a fixed list of known formats. `ExtractShapeFabricatedIds` then flags any token matching one of these shapes that isn't a real id and isn't already claimed by the legacy transaction-shape path (so no citation occurrence is ever double-counted between the two mechanisms — see the methods' own doc comments and `tests/AmlAgent.Tests/EvidenceReferenceScoringTests.cs`). Verified against real task-007 case data: a synthetic report citing a real transaction, a real relationship, a fabricated transaction, a fabricated relationship, and a fabricated watchlist entry correctly grounded the real ones and flagged all three fabrications, with `CitedDistinct` exactly matching the number of distinct tokens actually present (no inflation from the two extraction mechanisms overlapping).

**What this still can't do**, stated plainly rather than implied away: an evidence type with **zero real examples anywhere in `validEvidence`** has no shape to infer from, so a fabrication of that type remains invisible (e.g. inventing a SAR id in a report when the case has no real SAR records at all — there's nothing to generalise a SAR shape from). This is an honest structural limit, not a bug: the mechanism can only generalise from evidence the case actually contains. Like the original transaction-id regex, shape inference is also a heuristic that can, in principle, coincidentally match an ordinary word with trailing digits that was never meant as a citation — a known, accepted tradeoff of this style of detection, not new to this fix. The stronger long-term direction — moving agents toward **structured citation output** (`{"claim_id": ..., "evidence": [{"evidence_id": ..., "evidence_type": ...}]}`) rather than reverse-parsing prose — is not yet implemented as a benchmark condition; it is a planned next step (see `AML-Agent-Bench_v0.3_Validation_Priorities_for_Claude.md` item 4), not something this fix claims to deliver.

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

Distinct from evidence recall: recall asks "how much of the reference evidence did the agent cite anywhere"; coverage asks "how many of the agent's claims individually have adequate support". **Implemented and live (fix #7)** — `AmlAgent.Evidence.ClaimLevelScoring.ComputeClaimSupportCoverage`, wired into `EvidenceTraceabilityProfileBuilder.Build`'s `claims` parameter, is no longer only reachable from synthetic test fixtures.

**Where the claim-level input now comes from:** materiality and reference evidence (`Required`/`AcceptableAlternatives`) are **task-authored**, not LLM-guessed — a task's `evidence-annotations.json` can define a `material_claims` array (`tasks/task-007-multi-source-mule-network/evidence-annotations.json` is the first: six claims formalising that task's `expected_conclusions`, e.g. "N100 is the victim", each with a transaction-id evidence path and the equivalent relationship-graph path as an acceptable alternative). `JudgeAgent.cs` (`LoadMaterialClaims`) loads these templates and adds a MATERIAL CLAIMS section to the judge prompt asking the LLM for exactly one narrow thing per claim: which evidence ids the candidate's report actually cites in support of it (`material_claim_assessments`) — not whether the claim is material (already decided by the task author) and not what counts as adequate support (decided deterministically afterwards by `ClaimLevelScoring.IsSupported`'s Required/AcceptableAlternatives set comparison). The merged `Claim` objects are written to `judge_report.json`'s `material_claims` array (`AmlAgent.Evidence.ClaimJson.ToJsonArray`), and `AssuranceProfileBuilder.cs` reads them back (`ClaimJson.ParseArray`) to populate `claim_support_coverage` in `assurance_profile.json`. Verified against the real task-007 annotation file (not just synthetic fixtures): a throwaway harness loaded the actual six claims, fed two synthetic report scenarios through the real scoring path, and confirmed CSC = 1.0 (all six adequately supported, one via its acceptable-alternative relationship-graph path) and CSC = 0.6667 (two of six unsupported) computed correctly, surviving a round trip through `ClaimJson`.

Task-006 has no `material_claims` annotation, so `claim_support_coverage` stays `null` there — "not measured", not zero, exactly the existing null-safety discipline. Per the [LLM-as-judge boundary](#llm-as-judge-positioning) discipline (see also [fix #6](#traceability-failure-taxonomy)): the LLM's role here is narrowly scoped to citation identification, not adequacy judgement, so CSC computed this way is deterministic *given* the LLM's citation-identification output — it is not a purely deterministic measurement end-to-end, the same boundary that applies to EGHR's claim extraction.

### Structured citation output condition

**v0.3 validation-priorities item 4.** The paragraph above names the one remaining non-deterministic step in CSC under the default condition: an LLM identifying which evidence ids a free-text report cites for each material claim. This section is that boundary's counterpart, not another metric — an experimental **condition**, in the RQ4 sense (`docs/experimental-design.md`): does giving the agent a structured output contract, instead of asking it to write prose the judge then has to re-parse, change reference validity, claim-level precision/recall, Claim Support Coverage, or extraction reliability?

**Condition A (default, unchanged):** the agent writes only a free-text report (`mule_network_report.md`). The judge's own LLM call maps each material claim to the evidence ids the report cites for it (`material_claim_assessments`), exactly as CSC already worked before this item. `judge_report.json` records `"evidence_extraction_method": "llm_mapped_from_narrative"`.

**Condition B (new, opt-in):** the agent additionally produces `claim_evidence.json` — its own explicit claim-id/text/evidence declarations, in the schema `AmlAgent.Evidence.StructuredClaimEvidenceReader` parses. `claim_id` values are the SAME task-defined slot labels (`MC1`–`MC6` for task-007) the agent's free-text report is already asked to cover section-by-section — see `tasks/task-007-multi-source-mule-network/prompt.md`'s "Structured citation output (optional)" section. This does not give the agent any information about which evidence is *required* or what the correct answer is; it only restates the same investigative structure the prompt's report outline already implies, in a typed shape. When `claim_evidence.json` is present, `JudgeAgent.cs` uses the agent's own declarations **directly** as `AgentEvidence` — no LLM mapping step runs at all for claim-level scoring, and the MATERIAL CLAIMS section is dropped entirely from the judge prompt (one fewer thing asked of the LLM, not just one fewer thing used from its answer). `judge_report.json` records `"evidence_extraction_method": "structured_output"`. Under this condition, CSC is deterministic **end-to-end**, not just given the mapper's output — there is no mapper left to be non-deterministic.

**Comparing the two conditions:** `AmlAgent.Evidence.StructuredOutputConditionComparison.Compare` takes two `judge_report.json` documents (one per condition, same task/agent/model) and reports the raw difference in reference validity rate, `claim_support_coverage`, and claim-level precision/recall — never a single "structured output is better" verdict, the same discipline `JudgeVsHumanComparison`/`ClaimAnnotationAdjudication` already follow. It throws if either report's own `evidence_extraction_method` doesn't match the condition it was passed as, so a comparison can't silently run on two Condition-A runs by mistake.

**Verified against real task-007 data, not just synthetic fixtures:** the exact merge logic `JudgeAgent.cs` uses was run externally against the task's real `material_claims` templates with a synthetic (clearly-labelled) structured submission — a "perfect" submission citing exactly the required evidence for all six claims produced CSC = 1.0 with zero LLM involvement in the mapping, and a submission missing one claim's evidence produced CSC = 0.8333 (5/6), both surviving a round trip through `ClaimJson`.

**What this is not:** a claim that structured output *is* better, faster, or more reliable — that is an empirical question for a real repeated-run comparison (`docs/experimental-design.md`'s RQ4 programme) to answer, not something asserted here. No live run under either condition has been executed yet (that would spend real API tokens); what exists is the mechanism to run and compare both conditions the moment it is.

### Evidence Sufficiency Rate (ESR)

```
ESR = claims with sufficient supporting evidence / claims requiring evidence
```

A transaction may be valid and relevant yet insufficient to establish a multi-transaction conclusion such as layering, circularity, rapid movement, or temporal escalation. **Not yet implemented, deliberately (fix #8).** Sufficiency judgements are inherently semantic and require validated human annotation before they can be scored, deterministically or otherwise — the annotation *schema and fixtures* now exist (`AmlAgent.Evidence.SufficiencyAnnotationReader`, `validation/gold/sufficiency/`, see [docs/evidence-annotation-protocol.md#evidence-sufficiency-annotation-schema](evidence-annotation-protocol.md#evidence-sufficiency-annotation-schema)) so a real annotation round has somewhere to go, but `evidence_sufficiency_rate` remains an explicit `null` in `EvidenceTraceabilityProfileBuilder.Build` until that round has actually happened and been checked for inter-rater agreement. This is the same sequencing discipline [Claim Support Coverage](#claim-support-coverage-csc) followed in reverse order for a reason: CSC needed no semantic adequacy judgement beyond citation identification, so it was safe to wire live first; sufficiency does need one, so it stays unscored until validated, not implemented first and validated as an afterthought.

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

**Fix #6 — correcting an overstatement:** `invalid_reference` and `evidence_omission` are fully deterministic (citation-existence check against the case's evidence ids; gold-evidence-set comparison — no LLM involved in either). `unsupported_claim` is **not** deterministic in the same way, and describing it as such overstates what the pipeline actually does: whether a claim is `supported`/`unsupported`/`contradicted` is an LLM self-label (`EvidenceScoring.ScoreClaims`'s `claim.Support`, produced by the judge model reading the claim against the grounding data). The *only* deterministic part is a narrow backstop — a claim citing a fabricated (nonexistent) evidence id is force-overridden to `unsupported` regardless of what the LLM said, so the judge cannot inflate its own grounding by mislabelling a fabricated citation as supported. Every other `unsupported_claim` (a claim whose citations all exist, but the LLM judged it unsupported anyway) is a semantic judgement call, not a deterministic computation — it belongs conceptually with `evidence_mismatch`/`insufficient_evidence`/`overcitation` below as LLM-originated, constrained by (not derived purely from) a deterministic check. `evidence_mismatch`, `insufficient_evidence`, and `overcitation` require semantic judgement (see [LLM-as-judge positioning](#llm-as-judge-positioning) below) and are only partially scored today — one exception: `insufficient_evidence` failures generated from claim-level `ReferenceEvidence` (`ClaimLevelScoring.IsSupported`, wired into `EvidenceTraceabilityProfileBuilder.Build`'s optional `claims` parameter) ARE fully deterministic set-comparison, no LLM involved, whenever claim-level annotations exist for a task — none do in a live run today (see [Formal claim-evidence model](#formal-claim-evidence-model)), so this exception is currently theoretical rather than exercised. `traceability_break` is assessed structurally by the case-integrity/evidence-reference-validation layer (`AmlAgent.Adapters.Canonical.EvidenceIntegrityValidator`) for the canonical-data side of the pipeline, and is a genuinely open question for the agent-output side (see `validation/gold/discrimination/*/04_incorrect_conclusion_plausible_explanation.json` for a concrete case where a report is perfectly traceable yet its conclusion is wrong — traceability and correctness are not the same property; `validation/gold/discrimination/task-007/10_incorrect_outcome_excellent_traceability.json`, fix #9, is the same finding upgraded to an objectively, mechanically checkable wrong conclusion rather than a narratively-asserted one). `attribution_ambiguity` as a *detected failure* is **still not implemented** — nothing flags that an agent's report was itself ambiguous about which evidence set it relied on. What IS now implemented: the multiple-acceptable-evidence-set annotation (`ReferenceEvidence.AcceptableAlternatives`, see [Formal claim-evidence model](#formal-claim-evidence-model) above) means an agent citing a genuinely valid alternative set is no longer unfairly scored as if it omitted required evidence — `ClaimLevelScoring.IsSupported` credits either `Required` or any one `AcceptableAlternatives` set equally. What remains open is the reverse case this failure type actually names: the AGENT itself failing to clarify which of several plausible sets it's using, as opposed to the annotation now correctly allowing for more than one right answer.

## Legacy EGHR metric

The prototype's original primary metric, "Evidence-Grounded Hallucination Rate" (EGHR — `AmlAgent.Evidence.EvidenceScoring.ScoreClaims`), is **retained in code as a legacy/secondary metric, not deleted**, and is not described as a primary contribution of this PhD. It remains useful and is not silently removed because:

- it already implements a deterministic citation-existence backstop, which maps directly onto `invalid_reference` in the taxonomy above; that same backstop also *contributes to* `unsupported_claim` (any claim it force-overrides), but does not make `unsupported_claim` itself deterministic — most `unsupported_claim` labels come from the LLM judge's own support classification, not this backstop (see [Traceability failure taxonomy](#traceability-failure-taxonomy) above, fix #6);
- removing it would break existing tests and the existing assurance-profile output without adding measurement capability;
- `docs/research-scope-mapping.md` and `validation/gold/eghr/*.json` document its behaviour (including known definitional gaps, e.g. no distinct "partially supported" bucket) in detail already, and that validation work remains valid evidence about the deterministic backstop's correctness even though EGHR itself is no longer the headline construct.

Concretely: EGHR's `unsupported_count` and `contradicted_count` are the same signal as `unsupported_claim` and (for a citation that directly contradicts its own cited evidence) `evidence_mismatch`; EGHR's citation-existence override is the same signal as `invalid_reference`. Migration is staged, not a rename: EGHR fields stay live in `assurance_profile.json` and `judge_report.json`; new Evidence Traceability Profile fields are additive alongside them (see [docs/research-scope-mapping.md](research-scope-mapping.md) for the schema).

## Outcome correctness vs. task performance

**Fix #5.** H4 (`docs/experimental-design.md`) asks whether task performance is associated with evidence traceability — a question that requires the two variables to be measured independently. Before this fix, "task performance" meant the qualitative rubric's `overall_percentage`, and every task's rubric.json mixed outcome-correctness dimensions (did the agent identify the right victim/mule/exit accounts, typology, network path, and correctly clear innocent accounts) together with citation-quality dimensions (`evidence_grounding`, `avoids_unsupported_claims`, `evidence_traceability`, and similar). Since `evidence_traceability` is one of the rubric dimensions summed into `overall_percentage`, correlating "task performance" against the benchmark's deterministic evidence-traceability metric was partially correlating a variable against a component of itself — contaminating exactly the comparison H4 needs to make, and making any reported "discordant case" (high task performance, low traceability, or vice versa) partly definitionally impossible rather than a real empirical finding.

**Fix #9 makes the discordant cells concrete, not just conceptually possible.** Separating the constructs (this fix) only shows they *could* diverge; it doesn't show they *do*. `validation/gold/discrimination/task-007/09_correct_outcome_poor_traceability.json` and `.../10_incorrect_outcome_excellent_traceability.json` are controlled, hand-authored instances of the two off-diagonal cells — outcome correct but poorly cited, and outcome wrong but impeccably cited — with `DiscriminationValidationTests.DiscriminantValidity_Task007_TheTwoFamiliesAreMirrorImagesOfEachOther` asserting the two scores move in opposite directions across them. task-006 gets the same family-1 fixture (`07_correct_conclusion_poor_traceability.json`); its family-2 case was already covered by the pre-existing `04_incorrect_conclusion_plausible_explanation.json`, honestly flagged as narratively-asserted rather than structurally checked, since task-006's output has no entity-classification ground truth to check against.

**v0.3 item 7 makes the full matrix explicit, not just the two off-diagonal cells.** The two fixtures above are the interesting cells for H4, but a complete construct-validity argument needs all four:

| | High traceability | Low traceability |
|---|---|---|
| **High outcome correctness** | Cell A — `01_correct_answer_correct_evidence.json` | Cell B — `09_correct_outcome_poor_traceability.json` |
| **Low outcome correctness** | Cell C — `10_incorrect_outcome_excellent_traceability.json` | Cell D — `11_incorrect_outcome_poor_traceability.json` |

Cell D (`task-007`) and its task-006 counterpart (`08_incorrect_conclusion_poor_traceability.json`) are new — a deliberately "boring" control case where both constructs are bad together, confirming the scoring machinery doesn't need the two axes to disagree to work correctly; it's cells B and C that carry the actual discriminant-validity argument. `DiscriminationValidationTests.FourQuadrantMatrix_Task007_AllFourCellsExistAndAreCorrectlyPositioned` asserts all four cells exist, are pairwise distinct, and land in the outcome/traceability position the matrix says they should — the four-quadrant design as one explicit, checked structure, not four separate pairwise comparisons a reader has to piece together themselves.

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

## Schema versioning

**Fix #12** (extended by the v0.3 validation-priorities pass). Five schemas now each carry an explicit `schema_version`, versioned independently of one another:

| File / block | Field | Owner | Current value | Kind |
|---|---|---|---|---|
| `judge_report.json` (top-level) | `schema_version` | `agents/csharp-sk/Agent/JudgeAgent.cs` (`JudgeAgent.SchemaVersion`) | `"1.0"` | Generated output |
| `assurance_profile.json` → `evidence_traceability_profile` | `schema_version` | `AmlAgent.Evidence.EvidenceTraceabilityProfileBuilder.SchemaVersion` | `"1.0"` | Generated output |
| `assurance_profile.json` (top-level envelope) | `schema_version` | `AmlAgent.Harness.AssuranceProfileBuilder` (`AssuranceProfileBuilder.SchemaVersion`) | `"0.3"` | Generated output |
| `evidence-annotations.json`'s `material_claims` array | `material_claims_schema_version` | Declared by the task author; checked against `JudgeAgent.MaterialClaimsSchemaVersion` | `"1.0"` | Task-authored input |
| Sufficiency annotation files (`validation/gold/sufficiency/`) | `schema_version` | Declared by the annotator; checked against `SufficiencyAnnotationReader.CurrentSchemaVersion` | `"1.0"` | Externally-authored input |

`bench_result.json` (`schema_version: "1.0"`, `AmlAgent.Harness.ReportBuilder`) and `case_manifest.json`/canonical-case datasets (`AmlAgent.Adapters.Canonical.CanonicalSchema`, checked for mismatch by `CanonicalCaseMerger`) were already versioned before fix #12; the five above were the gap. None of them existed despite all three *generated* shapes having grown substantially across fixes #1–#9 (RVR, the precision/`valid_evidence_precision` split, `rubric_by_category`/`outcome_correctness`, `material_claims`, `claim_support_coverage`) with zero version signal at any point — exactly the silent-incompatibility risk this fix closes off.

**Generated vs. authored, and why the check differs:** for the three *generated* files (this codebase writes them), the version is simply stamped as a constant — there's nothing to check, since we control the writer. For the two *authored* schemas (a task author writes `material_claims`; an annotator writes a sufficiency-annotation file), the version is a required field the file must declare, checked against what the reading code was written against: `JudgeAgent.LoadMaterialClaims` **warns** (doesn't throw) on a missing/mismatched `material_claims_schema_version`, since a stale task-authoring file shouldn't crash a live benchmark run outright; `SufficiencyAnnotationReader.Parse` **throws** on a missing `schema_version`, since that reader is only ever invoked from tests/tooling processing a specific annotation file, where failing loudly and immediately is more useful than a silent partial read.

**Why five independent versions, not one shared number:** `evidence_traceability_profile` is re-derived inside `AssuranceProfileBuilder.Build` from `judge_report.json`'s fields — its shape can change (e.g. a new claim-level field) without the outer `assurance_profile.json` envelope's own top-level fields (`policy`, `deployment_decision`, `provenance`, ...) changing at all, and vice versa; the two annotation schemas are authored independently of any of the generated-output code entirely. A single shared version number would force every consumer to treat an unrelated change as a breaking one.

**Bump policy** (documented on each `SchemaVersion`/`CurrentSchemaVersion` constant, not just here): increment the MINOR component for additive, backward-compatible changes — a new field, or a field that was always `null` becoming sometimes-populated. Increment MAJOR for anything a consumer parsing the file/block would need to change code for: a field renamed, removed, or changing type/meaning. None of the five was retroactively bumped for past additive changes (fix #1's RVR, fix #4's precision split, fix #5's `rubric_by_category`, fix #7's `material_claims`) — they are baselined now, going forward, rather than backdated to a history no version field was ever tracking.

**What remains open:** no automated check yet consumes these version numbers when a file is *read back* (`aml-harness compare`, `ExperimentRepeatCommand`, `SufficiencyAnnotationReader` beyond its own file) to reject or warn on a genuine mismatch — today the fields exist and are populated/validated at the point of authoring or generation, but nothing downstream cross-checks two files' versions against each other yet.
