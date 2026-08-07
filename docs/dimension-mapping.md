# Proposal dimensions vs. current implementation

This page exists so there is one honest, checkable answer to "what does the
proposal say you'll build, and how much of it exists in the repo today?" It
is written for viva / supervisory review, not for end users of the bench.

The proposal (`Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf`)
frames AML-Agent-Bench as a three-year, six-dimension benchmark. The
codebase in this repo is the **existing prototype** the proposal is
de-risked by (§13, "Resources and Feasibility") — it is Year-0 seed work,
not the finished instrument. The table below maps each of the six
dimensions to what is implemented now, what is partial, and what is not
started.

| Dimension | Proposal metric | Current status | Where |
|---|---|---|---|
| Task performance | F1 / balanced accuracy | **Implemented** for both tasks as deterministic pass/fail rules (schema, range, sort, threshold), not yet F1/balanced-accuracy against a labelled multi-case set | `tests/AmlAgent.Tests/OutputContractTests.cs`, `Task006SummaryTests.cs` |
| Hallucination (EGHR) | Evidence-Grounded Hallucination Rate — atomic claim extraction + entailment vs. annotated evidence | **Implemented (task-006).** The judge extracts atomic claims and self-labels each supported/unsupported/contradicted; `AmlAgent.Evidence.EvidenceScoring.ScoreClaims` deterministically forces any claim citing a nonexistent transaction ID to "unsupported" so the LLM cannot inflate its own grounding. Reports a real rate, not a rubric score. First live run: 40.0% (2/5 claims unsupported, 0 contradicted). Limitation: no intrinsic-vs-extrinsic distinction beyond the citation-existence check — "contradicted" still relies on the judge's own semantic read of the data — and no human-audited sample yet (RQ3). | `src/AmlAgent.Evidence/EvidenceScoring.cs`, `agents/csharp-sk/Agent/JudgeAgent.cs`, tested in `tests/AmlAgent.Tests/EvidenceScoringTests.cs` (always-on unit tests) and `JudgeReportTests.cs` (live-run schema/consistency checks) |
| Evidence traceability | Citation precision / recall against gold evidence links | **Implemented (task-006).** Citation precision/recall computed entirely deterministically (regex citation extraction + set arithmetic against a curated gold-evidence set) — no LLM in this computation path. Gold set: `tasks/task-006-.../evidence-annotations.json` (13 SAR-linked / high-risk-destination transactions). First live run: precision 33.3%, recall 7.7% (1/13 gold citations matched), showing the six-dimension rubric's `evidence_citation: 3/5` and the real traceability recall tell different stories on the same report. Limitation: gold set is hand-curated by one author for one small dataset, not yet a multi-annotator, dataset-scale annotation (Phase 3). | Same as above | 
| Bias and fairness | Demographic-parity / equal-opportunity gap across matched counterfactual cases | **Not started.** No counterfactual case pairs, no protected-attribute variation, no disparity metric. | — |
| Explainability | Rubric score + faithfulness-via-perturbation check | **Partial.** `fact_vs_assumption` and `compliance_tone` rubric criteria give a plausibility rubric score. No faithfulness/perturbation test exists (i.e. nothing checks whether the *stated* reasoning matches what the agent actually used). | `tasks/task-006-.../rubric.json` |
| Auditability & trust | Audit-completeness %, Expected Calibration Error (ECE), run-to-run consistency | **Not started.** No audit-schema coverage check, no confidence elicitation or ECE, no repeated-run consistency measurement. `bench_result.json` does log the full agent/task/evaluator record, which is a starting point for an audit schema. | `src/AmlAgent.Harness/ReportBuilder.cs` |

## What else the proposal specifies that isn't in the repo yet

- **Judge-reliability controls** (RQ3 / Phase 6): human-annotated validation
  subset, inter-rater agreement, position/verbosity/self-enhancement bias
  checks per Zheng et al. (2023). The current judge is a single LLM call
  with defensively-recomputed arithmetic (so it can't inflate its own
  score) but has no human-agreement baseline.
- **Multiple agent architectures** (RQ4): the proposal compares single-agent
  tool use, retrieval-augmented, and multi-agent designs. Today there is one
  architecture (C#/Semantic Kernel single-agent tool-calling loop) plus one
  cross-language baseline (Python ReAct) — both single-agent tool-use, no
  RAG or multi-agent variant yet.
- **Public/synthetic research datasets** (Phase 3): AMLSim, NeurIPS
  synthetic AML data, Elliptic Bitcoin. Today's data is small, hand-authored
  synthetic CSVs built for determinism, not yet these datasets.
- **Regulatory-trust mapping deliverable** (Phase 7 / RQ5): a translation
  layer from benchmark evidence to FATF / EU AI Act / SR 11-7 requirements.
  Not started as a deliverable; the *grounding* references exist in the
  proposal and docs but no code maps a benchmark result to a specific
  regulatory clause.

## Why this gap is expected, not a problem

The proposal is explicit that Year 1 covers literature review, benchmark/
task design and dataset construction, with the harness *extension* (not
first build) happening across Years 1-2 (§12, Provisional Timeline). The
fact that a working C#/Semantic Kernel agent, a polyglot Docker harness,
two tasks and a dual deterministic + LLM-judge evaluator already run
end-to-end against live models (see
[docs/preliminary-results.md](preliminary-results.md)) is the feasibility
evidence cited in §13 of the proposal — it demonstrates the direction and
the capability to deliver, not the finished six-dimension instrument.

## Immediate next build priorities (in proposal order)

1. ~~Operationalise EGHR properly~~ — **done** for task-006 (see table
   above). Still needed: extend to task-001, and add a proper
   intrinsic/extrinsic split rather than relying on the judge's own
   "contradicted" label.
2. ~~Replace the citation-count check with true citation precision/recall~~
   — **done** for task-006 against a hand-curated gold set. Still needed:
   independent-annotator agreement on the gold set, and a gold set for
   task-001.
3. Add a counterfactual task variant (task family 3) to start RQ2 bias
   measurement.
4. Add a human-annotated judge-agreement sample to start RQ3 judge
   reliability work — including validating the LLM's own EGHR claim-support
   labels against human judgement, not just the citation-existence check.
