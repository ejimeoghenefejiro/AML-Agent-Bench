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
| Reference validity (RVR) | Fabricated-citation detection implemented deterministically (`EvidenceScoring.ComputeTraceability`'s `FabricatedCitations`); RVR itself (as a named rate) not yet surfaced as its own field | Task-specific gold sets only; RVR isn't computed as a distinct rate in output today, only the raw fabricated-citation list/count | Surface RVR explicitly in the Evidence Traceability Profile output; generalise beyond the current two annotated tasks |
| Evidence precision (EP) | Implemented, report-level (micro), for task-006 and task-007 | Flat gold set, report-level only — no claim-level precision yet | Claim-level EP + multi-annotator gold |
| Evidence recall (ER) | Implemented, report-level (micro), for task-006 and task-007 | Same as precision | Claim-level ER + multi-annotator gold |
| Evidence Traceability F1 (ETF1) | Implemented, aggregate only | Aggregate, not claim-level | Macro/micro and claim-level variants |
| Claim support coverage (CSC) | **Not implemented** | — | Implement — needs material-claim identification methodology first |
| Evidence sufficiency (ESR) | **Not implemented** | Inherently semantic, needs validated annotation | Define + validate against human judgement |
| Traceability failure taxonomy | Partial — `invalid_reference`/`unsupported_claim`/`evidence_omission` deterministically detectable via EGHR's citation-existence override and gold-set comparison; `evidence_mismatch`/`insufficient_evidence`/`overcitation` need semantic judgement, not yet scored; `traceability_break` assessed for canonical case data (`EvidenceIntegrityValidator`), open question for agent-output side | EGHR-oriented; not yet reorganised into the full 7-type taxonomy in code | Refactor scoring output to emit typed `traceability_failures`, not just EGHR's supported/unsupported/contradicted buckets |
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
- **Multi-source, multi-annotator gold evidence:** task-007's multi-source case (transactions + KYC + relationships + watchlist) exists and is structurally validated (`AmlAgent.Adapters.Canonical.EvidenceIntegrityValidator`), but its gold evidence set is still single-author, same limitation as task-006.

## Why this gap is expected, not a problem

A working C#/Semantic Kernel agent, a polyglot Docker harness, a multi-format data-adapter layer, three tasks spanning three complexity levels, and a dual deterministic + LLM-judge evaluator already run end-to-end against live models (see [docs/preliminary-results.md](preliminary-results.md)). That is feasibility evidence for the measurement framework — it demonstrates the direction and the capability to deliver, not the finished, validated instrument the research design describes.

## Immediate next build priorities

1. Surface Reference Validity Rate as an explicit field, not just the raw fabricated-citation list.
2. Add claim-level evidence precision/recall/coverage (currently report-level/micro only).
3. Execute a real, independent-annotator gold-evidence round for at least one task, to start convergent-validity work.
4. Run `aml-harness experiment repeat` / `experiment judge-repeat` at a statistically meaningful batch size (the current live evidence is a 2-run proof of concept, not a reliability study).
5. Build the noise/distractor task variants described in `docs/experimental-design.md` and `validation/experiments/README.md`.

## Planned claim-level schema

Sketch of the claim-level data model referenced from
[docs/evidence-traceability-framework.md](evidence-traceability-framework.md#formal-claim-evidence-model)
and [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md#multiple-valid-gold-handling)
— **not yet implemented**, shown here so the shape is concrete rather than
only described in prose:

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
      },
      "reference_valid": true,
      "evidence_relevant": true,
      "evidence_sufficient": null
    }
  ]
}
```

Exact field names may change; the requirement this schema must satisfy is
claim-level analysis with multiple acceptable evidence sets, which flat
`gold_evidence_txn_ids` lists (today's actual format) cannot represent. The
current `evidence_traceability_profile` block in `assurance_profile.json`
(`AmlAgent.Evidence.EvidenceTraceabilityProfileBuilder`) is report-level, not
claim-level — it is the additive Phase B step described in this document's
own "Immediate next build priorities" above; this claim-level schema is the
Phase C step that has not been started.

## Related work: the assurance-profile prototype

A related but distinct initiative is built on top of the metrics described above: `assurance/` — a machine-readable, policy-evaluated "AML Agent Assurance Profile", positioned as a **downstream application** of evidence-traceability measurement, not part of the doctoral core (see [assurance/README.md](../assurance/README.md#positioning-relative-to-the-phd)). It reuses the same metrics (EGHR/traceability F1, fabricated citations, task performance) plus case-level evidence-integrity validation, evaluated against a configurable policy, producing a `PASS` / `PASS_WITH_CONDITIONS` / `NOT_READY_FOR_DEPLOYMENT` deployment decision — with `compare`, `regress`, `load-case`, and `experiment` CLI commands for cross-run analysis. Same honesty discipline as this page: dimensions this repo doesn't measure (fairness, faithfulness, audit completeness, calibration, consistency) are marked `not_implemented` in every generated profile, never faked.
