# Research scope vs. current implementation

> Formerly `docs/dimension-mapping.md`. Retitled and restructured around the
> [Evidence Traceability Profile](evidence-traceability-framework.md) — the
> repository's research framing changed from six trustworthy-AI dimensions to
> evidence traceability as the sole primary doctoral construct; this page now
> tracks components of *that* framework, not the old dimension table. A
> compatibility stub remains at the old path.

This page exists so there is one honest, checkable answer to "what does the
research design say you'll build, and how much of it exists in the repo
today?" It is written for viva / supervisory review, not for end users of the
bench.

The codebase in this repo is the **existing prototype** the PhD's measurement
framework is de-risked by — it is early-stage seed work, not the finished
instrument. The table below maps each component of the Evidence Traceability
Profile (see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md))
to what is implemented now, what is partial, and what is not started.

| Research component | Current implementation | Limitation | Required PhD work |
|---|---|---|---|
| Reference validity (RVR) | Implemented: `reference_validity_rate`, an explicit field in `evidence_traceability_profile` (`EvidenceTraceabilityProfileBuilder`), derived from `EvidenceScoring.ComputeTraceability`'s `FabricatedCitations` | Task-specific gold sets only | Generalise beyond the current two annotated tasks (task-006, task-007 — task-001 has no rubric/gold evidence at all) |
| Evidence precision (EP) | Implemented at BOTH levels: report-level (micro, `evidence_precision`) for task-006/task-007, and claim-level (macro, `claim_level_precision`) via `ClaimLevelScoring.ComputeClaimLevelTraceability` — both real, tested, distinct fields. Report-level precision also disambiguates its own denominator (fix #4): `evidence_precision`/`evidence_traceability_f1` count fabricated citations against the denominator (standard IR definition, the primary reported number), while `valid_evidence_precision`/`valid_evidence_f1` preserve the original real-citations-only formula under an explicit name — see [docs/evidence-traceability-framework.md#evidence-precision-ep](evidence-traceability-framework.md#evidence-precision-ep) | Claim-level EP needs real claim-level gold annotation to run against live data; today it only has synthetic test fixtures | Multi-annotator gold at claim level (see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md)) |
| Evidence recall (ER) | Same status as precision — report-level and claim-level both implemented (`evidence_recall`, `claim_level_recall`) | Same as precision | Multi-annotator gold at claim level |
| Evidence Traceability F1 (ETF1) | Implemented at both levels (`evidence_traceability_f1` report-level, `claim_level_f1` claim-level macro) | Claim-level needs real annotation data to populate in a live run | Same as above |
| Claim support coverage (CSC) | **Implemented and live (fix #7)**: `claim_support_coverage` field, `ClaimLevelScoring.ComputeClaimSupportCoverage`, populated in real `judge_report.json`/`assurance_profile.json` output for task-007 via task-authored `material_claims` (materiality/reference evidence) plus the judge's per-claim citation identification — see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md#claim-support-coverage-csc) | Only task-007 has a `material_claims` annotation; task-006 stays `null`. Single-author annotation, not yet multi-annotator | Annotate task-006 (or a new task) at claim level too; multi-annotator claim-level gold for convergent-validity work |
| Evidence sufficiency (ESR) | **Not implemented** | Inherently semantic, needs validated annotation | Define + validate against human judgement |
| Traceability failure taxonomy | Partial — `invalid_reference`/`evidence_omission` are fully deterministic (citation-existence check, gold-set comparison, no LLM). `unsupported_claim` is **LLM-originated** (the judge self-labels each claim supported/unsupported/contradicted), constrained by only a narrow deterministic backstop that force-overrides a claim citing a fabricated id to unsupported — see [docs/evidence-traceability-framework.md#traceability-failure-taxonomy](evidence-traceability-framework.md#traceability-failure-taxonomy) (fix #6, correcting a prior overstatement here). `evidence_mismatch`/`overcitation` need semantic judgement, not yet scored; `insufficient_evidence` IS deterministic when claim-level `ReferenceEvidence` exists (no live task has it yet); `traceability_break` assessed for canonical case data (`EvidenceIntegrityValidator`), open question for agent-output side | EGHR-oriented; not yet reorganised into the full 7-type taxonomy in code | Refactor scoring output to emit typed `traceability_failures`, not just EGHR's supported/unsupported/contradicted buckets |
| Provenance/reproducibility | Strong prototype — dataset/case hashing (`CanonicalHashing`), git SHA, benchmark version, format-invariance and source-order-invariance proven with real live-DB verification (`tests/AmlAgent.ResearchValidation/FormatInvarianceTests.cs`, `SourceOrderInvarianceTests.cs`), determinism explicitly tested (`DeterminismTests.cs`) | Versioning of the schema itself (not the data) is informal | Formalise schema versioning for `assurance_profile.json` / `case_manifest.json` |
| Human validation | Schema, loader, and comparison tooling built and tested against a synthetic fixture (`HumanAnnotation.cs`, `JudgeVsHumanComparison.cs`, `tests/AmlAgent.ResearchValidation/HumanAnnotationTests.cs`); no real annotations collected | Zero real human annotation data exists yet | Design + execute a real annotation round (see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md)) |
| Controlled interventions | Design documented (`docs/experimental-design.md`); repeated-run and judge-repeatability runner built and demonstrated live with real API calls (`aml-harness experiment repeat` / `experiment judge-repeat`) | Only a 2-run demo executed; no intervention conditions (citation-required, retrieval-constrained, etc.) implemented as distinct agent variants yet | Design + execute at meaningful scale |

## Task-level detail (task performance, not a traceability-profile component)

| Component | Current status | Where |
|---|---|---|
| Task performance | **Implemented** for all three tasks as deterministic pass/fail rules (schema, range, sort, threshold), not yet F1/balanced-accuracy against a labelled multi-case set | `tests/AmlAgent.Tests/OutputContractTests.cs`, `Task006SummaryTests.cs` |

## Concrete first results (kept for record, see docs/preliminary-results.md for interpretation)

- **EGHR (legacy metric, now the `unsupported_claim`/`invalid_reference` signal — see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md#legacy-eghr-metric)):** first live run on task-006, 40.0% (2/5 claims unsupported, 0 contradicted).
- **Evidence traceability (precision/recall):** first live run on task-006, precision 33.3%, recall 7.7% (1/13 gold citations matched) — the task's holistic rubric scored `evidence_citation: 3/5` on the same report, illustrating exactly the discriminant-validity question this PhD studies (see [docs/validation-plan.md](validation-plan.md#discriminant-validity)).
- Both numbers are hand-curated-gold-set, single-run, single-task results — feasibility evidence, not a validated benchmark result. See [docs/validation-plan.md](validation-plan.md) for what would need to happen before either number supports a general claim.

## What else the research design specifies that isn't in the repo yet

- **Judge-reliability controls** (RQ3): human-annotated validation subset, inter-rater agreement, position/verbosity/self-enhancement bias checks per Zheng et al. (2023). The judge-repeatability runner (`experiment judge-repeat`) exists and has captured real variance on a small demo; no human-agreement baseline exists yet.
- **Multiple agent architectures** (RQ3/RQ4): today there is one architecture (C#/Semantic Kernel single-agent tool-calling loop) plus one cross-language baseline (Python ReAct) — both single-agent tool-use, no retrieval-augmented, evidence-constrained, or verifier-assisted variant yet.
- **Public/synthetic research datasets:** AMLSim, NeurIPS synthetic AML data, Elliptic Bitcoin. Today's data is small, hand-authored synthetic CSV/JSON/Parquet built for determinism, not yet these datasets.
- **Multi-source, multi-annotator gold evidence:** task-007's multi-source case (transactions + KYC + relationships + watchlist) exists and is structurally validated (`AmlAgent.Adapters.Canonical.EvidenceIntegrityValidator`), and its gold evidence set now spans evidence types (`gold_evidence_ids`: transaction, relationship, and watchlist ids, not just transactions), but it is still single-author and report-level (no claim-level annotation yet) — same annotator-count limitation as task-006.

## Why this gap is expected, not a problem

A working C#/Semantic Kernel agent, a polyglot Docker harness, a multi-format data-adapter layer, three tasks spanning three complexity levels, and a dual deterministic + LLM-judge evaluator already run end-to-end against live models (see [docs/preliminary-results.md](preliminary-results.md)). That is feasibility evidence for the measurement framework — it demonstrates the direction and the capability to deliver, not the finished, validated instrument the research design describes.

## Immediate next build priorities

1. ~~Surface Reference Validity Rate as an explicit field~~ — **done**: `reference_validity_rate` in `evidence_traceability_profile` (`AmlAgent.Evidence.EvidenceTraceabilityProfileBuilder`).
2. ~~Generalise evidence beyond transaction IDs~~ — **done**, including the live judge: `AmlAgent.Evidence.EvidenceReference` + `ComputeTraceability(string, IReadOnlyCollection<EvidenceReference>, ...)` recognise citations to any canonical evidence type (relationship, SAR, watchlist, account, entity, ...), not just transactions, and `agents/csharp-sk/Agent/JudgeAgent.cs` now reloads a workspace's `case-definition.json` (when present) to score task-007 against that full universe instead of flat transaction-id grounding files — see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md#evidence-node-realised-evidencereference). Task-006 and every task without a `case-definition.json` are unaffected (same flat-file path as before).
3. ~~Add claim-level evidence precision/recall/coverage~~ — **done, live for task-007 (fix #7)**: `ClaimLevelScoring` was already implemented and tested at the library level; `JudgeAgent.cs` now loads task-authored `material_claims` (materiality + Required/AcceptableAlternatives reference evidence) from `evidence-annotations.json`, asks the judge only to identify per-claim citations, and writes the merged `Claim` objects to `judge_report.json` for `AssuranceProfileBuilder.cs` to score — see [docs/evidence-traceability-framework.md#claim-support-coverage-csc](evidence-traceability-framework.md#claim-support-coverage-csc). Next: Evidence Sufficiency Rate (ESR), deliberately built after CSC since ESR needs validated human judgement and CSC does not.
4. Execute a real, independent-annotator gold-evidence round for at least one task, to start convergent-validity work.
5. Run `aml-harness experiment repeat` / `experiment judge-repeat` at a statistically meaningful batch size (the current live evidence is a 2-run proof of concept, not a reliability study).
6. Build the noise/distractor task variants described in `docs/experimental-design.md` and `validation/experiments/README.md`.
7. ~~Separate task performance from traceability~~ — **done**: rubric.json dimensions now carry an optional `category` (`outcome_correctness`/`evidence_quality`/`process_quality`); `judge_report.json` gains `rubric_by_category` and a top-level `outcome_correctness` field via `AmlAgent.Evidence.RubricCategoryScoring`, and `assurance_profile.json` exposes `outcome_correctness_percentage` as a metric distinct from the full-rubric `task_performance_percentage` — see [docs/evidence-traceability-framework.md#outcome-correctness-vs-task-performance](evidence-traceability-framework.md#outcome-correctness-vs-task-performance). H4/H6 in `docs/experimental-design.md` now name `outcome_correctness_percentage` explicitly as the variable to use, since the old `task_performance_percentage` includes `evidence_traceability` as one of its own rubric dimensions and would contaminate any correlation against the deterministic traceability metrics.

## Proposed version milestone

**AML-Agent-Bench v0.2 — Evidence Traceability Core.** A research-facing release that removes hallucination and bias from the benchmark identity, introduces a typed claim-evidence model, expands deterministic traceability metrics, formalises gold annotations, and adds controlled traceability perturbation tests. Several pieces of this milestone are already done (the additive `evidence_traceability_profile` schema, the retitled research docs, EGHR relabelled legacy); the claim-level model, formal annotation round, and perturbation-test suite remain — see the priority list above and `docs/experimental-design.md`'s three-year programme for sequencing.

## Planned claim-level schema

**Implemented as C# types** — `AmlAgent.Evidence.Claim` and `ReferenceEvidence`
(see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md#formal-claim-evidence-model)
and [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md#multiple-valid-gold-handling)).
The JSON shape below (what a `judge_report.json`/annotation file carrying this
data would look like) matches the implemented model field-for-field, except
`reference_valid`/`evidence_relevant`/`evidence_sufficient` are not yet
separate output fields — `ClaimLevelScoring.Score`/`IsSupported` compute an
equivalent judgement (`supported`, `precision`, `recall` per claim) rather
than three separate booleans:

```json
{
  "claims": [
    {
      "claim_id": "C001",
      "text": "...",
      "material": true,
      "agent_evidence_ids": ["T001", "T002"],
      "reference_evidence": {
        "required": ["T001"],
        "acceptable_alternatives": [["T003", "T004"]],
        "corroborating": ["T005"]
      }
    }
  ]
}
```

**What's still not implemented:** no code today PRODUCES this JSON shape from
a live run. `judge_report.json`'s existing `claims` array (from the LLM
judge's EGHR extraction) has no `claim_id`/`material`/`reference_evidence`.
No task's `evidence-annotations.json` has real claim-level reference evidence
annotated. `EvidenceTraceabilityProfileBuilder.Build`'s `claims` parameter
(the wiring point for this data) is ready and tested, but has no live caller
passing it yet — see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md#formal-claim-evidence-model).
Two concrete next steps close this: extend the judge to emit this shape, and
annotate at least one task's gold evidence at claim level.

## Related work: the assurance-profile prototype

A related but distinct initiative is built on top of the metrics described above: `assurance/` — a machine-readable, policy-evaluated "AML Agent Assurance Profile", positioned as a **downstream application** of evidence-traceability measurement, not part of the doctoral core (see [assurance/README.md](../assurance/README.md#positioning-relative-to-the-phd)). It reuses the same metrics (EGHR/traceability F1, fabricated citations, task performance) plus case-level evidence-integrity validation, evaluated against a configurable policy, producing a `PASS` / `PASS_WITH_CONDITIONS` / `NOT_READY_FOR_DEPLOYMENT` deployment decision — with `compare`, `regress`, `load-case`, and `experiment` CLI commands for cross-run analysis. Same honesty discipline as this page: dimensions this repo doesn't measure (fairness, faithfulness, audit completeness, calibration, consistency) are marked `not_implemented` in every generated profile, never faked.
