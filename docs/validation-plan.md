# Validation Plan

> How AML-Agent-Bench's evidence-traceability measurements will be validated —
> not just implemented. See [docs/evidence-traceability-framework.md](evidence-traceability-framework.md)
> for what is being validated, and `validation/` (the `AmlAgent.ResearchValidation`
> test project) for work already done against several of these criteria.

## Content validity

Whether benchmark tasks, evidence types, failure modes, and metrics adequately represent evidence traceability in AML investigation. Evidence for this includes:

- literature-derived requirements (FATF, EU AI Act, SR 11-7 — see [docs/research-problem.md](research-problem.md));
- supervisor review;
- AML practitioner review — **not yet obtained**;
- structured expert feedback — **not yet obtained**.

## Construct validity

Whether the metrics change in expected directions under controlled manipulations. This is the one validity category with substantial evidence already: `tests/AmlAgent.ResearchValidation/DiscriminationValidationTests.cs`, `EvidenceCorruptionSensitivityTests.cs`, and the `validation/gold/discrimination/` and `validation/gold/traceability/` fixtures already demonstrate, with real hand-computed expected values:

- removing citations → recall/coverage falls;
- adding irrelevant citations → precision falls;
- deleting material evidence (via `EvidenceCorruptionSensitivityTests`) → traceability/evidence-integrity detection fires where the corruption breaks a reference, and is honestly shown to be invisible where it does not (see that file's own findings on single-source, uncorroborated value corruption);
- inserting nonexistent IDs → reference-validity/fabricated-citation count rises, though current precision/recall arithmetic does *not* fall as a result — a genuine, documented finding, not an oversight (`validation/gold/traceability/04_fabricated_evidence_ids.json`);
- weakening a multi-record evidence package → sufficiency should fall — **not yet measurable**, since evidence sufficiency itself is not yet implemented (see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md#evidence-sufficiency-rate-esr)).

This existing test suite is feasibility/construct-validity evidence for a research prototype, not a completed validation study — see [Discriminant validity](#discriminant-validity) below for the caveat that applies to all of it.

## Convergent validity

Compare benchmark traceability scores with independent human judgements of evidence quality/traceability. **Not yet performed** — requires real human annotations (see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md)), which do not exist yet. The comparison tooling to run this once annotations exist is already built and tested against synthetic fixtures (`JudgeVsHumanComparison`, `tests/AmlAgent.ResearchValidation/HumanAnnotationTests.cs`).

## Discriminant validity

Demonstrate that evidence traceability is not redundant with conventional task performance. The current preliminary result — a holistic rubric passing while traceability recall is very low (see `docs/preliminary-results.md`) — is useful feasibility evidence that the two can diverge, **but it is one run against one task and must not be described as conclusive validation**. A proper discriminant-validity claim requires the repeated-run and cross-model/cross-task experiments described in [docs/experimental-design.md](experimental-design.md).

## Reliability

Investigate:

- run-to-run variation — infrastructure built and demonstrated live (`src/AmlAgent.Harness/ExperimentRepeatCommand.cs`; see `validation/experiments/README.md`), but only a 2-run demo has been executed, not a statistically meaningful batch;
- annotation agreement — **not yet performed** (no multi-annotator data exists yet);
- robustness to evidence ordering — implemented and proven for the canonical-case merge layer (`tests/AmlAgent.ResearchValidation/SourceOrderInvarianceTests.cs`, including the important negative case: order-invariance does *not* hold across a genuine unresolved cross-source conflict, by design);
- robustness to semantically equivalent task wording — **not yet tested**;
- evaluator/judge variation where semantic scoring is used — infrastructure built and demonstrated live (`ExperimentJudgeRepeatCommand.cs`), same caveat as run-to-run variation above: real, measured variance exists in the 2-run demo, but no statistically meaningful batch has been run.

## Reproducibility

Ensure benchmark artefacts can reconstruct: task version, dataset version, agent version, model configuration, evidence annotations, evaluation configuration, and the final score/profile. Substantially implemented already — dataset/case hashing (`AmlAgent.Adapters.Normalisation.CanonicalHashing`), git commit SHA and benchmark version in `assurance_profile.json`, and format/order-invariance of the canonical hash are all tested (`tests/AmlAgent.ResearchValidation/FormatInvarianceTests.cs`, `SourceOrderInvarianceTests.cs`, `DeterminismTests.cs`). See [docs/research-scope-mapping.md](research-scope-mapping.md) for exactly which reproducibility indicators are live today.

## What "validated" does not mean here

None of the above sections should be read as a completed validation study. Where a bullet says "not yet performed," it means exactly that — no result exists to report, positive or negative. Where a bullet cites existing test evidence, that evidence supports feasibility and construct validity for a research prototype; it is not a substitute for the human-convergence, cross-model, and cross-task studies [docs/experimental-design.md](experimental-design.md) describes as still to be run.
