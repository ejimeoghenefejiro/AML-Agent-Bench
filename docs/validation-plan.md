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

## Inter-rater reliability

Whether gold annotations are reproducible across annotators. **Not yet performed** — no multi-annotator data exists yet. Evidence for this, once collected, would be agreement statistics and adjudication analysis (Cohen's kappa for two categorical raters, Krippendorff's alpha for more flexible multi-rater settings — see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md#multi-annotator-validation)). `JudgeVsHumanComparison.CompareAnnotators` (tested against a synthetic fixture in `tests/AmlAgent.ResearchValidation/HumanAnnotationTests.cs`) is the tooling ready to compute raw agreement once real multi-annotator data exists — deliberately not a chance-corrected statistic yet, since that would be premature against synthetic data.

## Test-retest reliability

Whether scores are stable under *identical, deterministic* conditions — a narrower claim than run-to-run agent variance (see [Reliability](#reliability) below), specifically about whether the **scorer itself** ever produces a different result from the same fixed inputs. This is the one reliability category with strong, direct evidence already: `tests/AmlAgent.ResearchValidation/DeterminismTests.cs` explicitly repeats canonical hashing, evidence-reference validation, citation precision/recall/F1, fabricated-ID detection, case-integrity evaluation, and policy evaluation 10× each against fixed inputs and asserts every run agrees exactly. Model stochasticity (the LLM's own output varying) is deliberately excluded from this category — that is [Reliability](#reliability) below, a property of the *agent and judge*, not the *scorer*.

## Reliability

Investigate:

- run-to-run variation — infrastructure built and demonstrated live (`src/AmlAgent.Harness/ExperimentRepeatCommand.cs`; see `validation/experiments/README.md`), but only a 2-run demo has been executed, not a statistically meaningful batch;
- robustness to evidence ordering — implemented and proven for the canonical-case merge layer (`tests/AmlAgent.ResearchValidation/SourceOrderInvarianceTests.cs`, including the important negative case: order-invariance does *not* hold across a genuine unresolved cross-source conflict, by design);
- robustness to semantically equivalent task wording — **not yet tested**;
- evaluator/judge variation where semantic scoring is used — infrastructure built and demonstrated live (`ExperimentJudgeRepeatCommand.cs`), same caveat as run-to-run variation above: real, measured variance exists in the 2-run demo, but no statistically meaningful batch has been run.

## Sensitivity

Whether the benchmark can distinguish known, graded levels of traceability degradation — not just detect *that* something is wrong, but track *how much*. Proposed test: synthetic perturbation ladders with graded evidence corruption (e.g. 0%, 25%, 50%, 100% of gold citations deleted; 0, 1, 3, 5 fabricated ids injected) and verify the relevant metric degrades monotonically. Partial evidence exists today at the qualitative level (`EvidenceCorruptionSensitivityTests.cs` shows individual corruptions ARE or ARE NOT detected — a binary, not a graded, result); a genuine graded-ladder sensitivity study has **not yet been built**.

## External validity

Whether results generalise beyond a single toy dataset. **Not yet demonstrated** — the current tasks use small, hand-authored synthetic data (see [docs/research-problem.md](research-problem.md#datasets)); the planned test is multiple AML task families plus multiple public/synthetic data sources (AMLSim, NeurIPS synthetic AML data, Elliptic Bitcoin), subject to licensing and ground-truth constraints that have not yet been resolved.

## Reproducibility

Ensure benchmark artefacts can reconstruct: task version, dataset version, agent version, model configuration, evidence annotations, evaluation configuration, and the final score/profile. Substantially implemented already — dataset/case hashing (`AmlAgent.Adapters.Normalisation.CanonicalHashing`), git commit SHA and benchmark version in `assurance_profile.json`, and format/order-invariance of the canonical hash are all tested (`tests/AmlAgent.ResearchValidation/FormatInvarianceTests.cs`, `SourceOrderInvarianceTests.cs`, `DeterminismTests.cs`). See [docs/research-scope-mapping.md](research-scope-mapping.md) for exactly which reproducibility indicators are live today.

## What "validated" does not mean here

None of the above sections should be read as a completed validation study. Where a bullet says "not yet performed," it means exactly that — no result exists to report, positive or negative. Where a bullet cites existing test evidence, that evidence supports feasibility and construct validity for a research prototype; it is not a substitute for the human-convergence, cross-model, and cross-task studies [docs/experimental-design.md](experimental-design.md) describes as still to be run.
