# AML-Agent-Bench: A Benchmark for Evidence Traceability in Autonomous AI Agents for Anti-Money Laundering Investigations

> **Thesis abstract.** Canonical, citable form, aligned to the PhD Research
> Proposal (`Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf`). The
> same framing appears in the repository [README](../README.md#abstract).
> When this document and the README diverge, **this file is authoritative**.

---

## One-sentence definition

This PhD develops and validates a benchmark for measuring whether the investigative conclusions produced by autonomous AML agents can be reliably traced to the transaction-level and case-level evidence that supports them.

## Abstract

Artificial intelligence is increasingly being explored for Anti-Money Laundering (AML) investigation, while emerging autonomous AI agents extend conventional machine-learning systems by retrieving records, invoking analytical tools, reasoning across multiple steps, and generating investigative conclusions. Existing AML evaluation has largely focused on predictive performance, while general-purpose agent benchmarks predominantly assess task completion and tool use. These measures do not establish whether the evidentiary basis of an agent's investigative conclusions can be independently reconstructed and verified.

This research addresses this problem through the design, implementation, and validation of **AML-Agent-Bench**, a domain-specific benchmark for measuring evidence traceability in autonomous AI agents performing AML investigations. Evidence traceability is defined as the degree to which material investigative claims can be linked to identifiable, valid, relevant, and sufficient evidence within the underlying case record. The research operationalises this construct through claim–evidence mappings and measures covering evidence-reference validity, precision, recall, claim-support coverage, and evidentiary sufficiency.

A design-science methodology is combined with controlled experimental evaluation. AML investigation scenarios will be constructed from synthetic and appropriate public financial-crime datasets and annotated with validated reference evidence sets. Annotation procedures will be assessed through independent human review and inter-rater agreement where feasible. Autonomous agents employing different underlying language models and evidence-oriented architectures will then be evaluated under comparable investigative conditions. Experiments will examine the effects of model choice, task complexity, and traceability-oriented interventions on evidentiary performance, while also examining the relationship between conventional task success and evidence traceability.

The research will contribute a formal framework for evidence traceability in agentic AML investigation, a measurement methodology together with its empirical validation, evidence concerning the evidentiary performance of contemporary AI agents, and an open-source benchmark implementation. AML-Agent-Bench is not intended to determine whether AI agents should autonomously make financial-crime decisions; it is intended to provide reproducible evidence concerning whether their investigative outputs can be independently traced and reviewed before supporting human decision-making.

## What evidence traceability means

Evidence traceability is the degree to which material claims produced by an autonomous AML agent can be systematically linked to identifiable, valid, relevant, and sufficient evidence within the underlying investigation record, such that the evidentiary basis of the conclusion can be independently reconstructed and reviewed. See [docs/evidence-traceability-framework.md](evidence-traceability-framework.md) for the full formal definition, the claim–evidence model, and the traceability failure taxonomy.

## Research questions

**RQ1 — Conceptualisation.** How should evidence traceability in autonomous AML-agent investigations be conceptualised and operationalised at claim and evidence level?

**RQ2 — Measurement and validation.** To what extent can evidence traceability be measured reliably using claim-level evidence validity, precision, recall, coverage, and sufficiency measures against validated reference evidence?

**RQ3 — Empirical variation.** How does evidence-traceability performance vary across underlying language models, agent architectures, AML task types, and task-complexity levels?

**RQ4 — Improvement interventions.** Which agent-design interventions improve evidence traceability without materially degrading AML task performance?

## Current prototype

The open-source codebase in this repository is the working foundation the research is built on and de-risked by, not yet the finished measurement instrument the proposal describes. It currently implements a C#/.NET 8 agent core on Microsoft Semantic Kernel, a polyglot Docker benchmark harness, a multi-format data-adapter layer (CSV/JSON/Parquet/SQL Server/PostgreSQL/Neo4j/GraphML/REST) feeding a canonical AML case model, three AML tasks spanning static graph-clustering (`aml-transaction-network`, no judge/rubric — xUnit-only), temporal anomaly detection (`task-006`), and multi-source mule-network investigation (`task-007`), deterministic xUnit scoring, and an LLM-as-judge that grades a compliance-style report against a task rubric.

On top of that rubric, the judge also computes deterministic evidence-traceability measures directly: citation precision/recall against a curated gold-evidence set (`src/AmlAgent.Evidence/EvidenceScoring.cs`), and a claim-level unsupported/fabricated-citation check (the legacy "Evidence-Grounded Hallucination Rate", EGHR — see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md#legacy-eghr-metric) for how it now maps onto the traceability failure taxonomy). Both are live for `task-006` and `task-007`, the two tasks with a `rubric.json` and curated `evidence-annotations.json`; `aml-transaction-network` has neither, so no judge or evidence-traceability measurement runs against it. Claim-support coverage is also live for `task-007` (task-authored material claims scored deterministically against the report's cited evidence). Evidence sufficiency has an annotation schema and fixtures but no scoring implementation yet, deliberately — it needs a validated human-annotation round first, which has not happened. Multi-annotator gold-evidence validation (more than one independent annotator, with inter-rater agreement measured) has not been performed for any task. See [docs/research-scope-mapping.md](research-scope-mapping.md) for the honest, component-by-component mapping between what is proposed and what is implemented today.

---

**Keywords:** agentic AI · autonomous AI agents · anti-money laundering · evidence traceability · claim–evidence mapping · benchmark evaluation · auditability · reproducibility · RegTech · LLM-as-judge · Semantic Kernel · FATF · EU AI Act

## Citation

If you reference this work, please cite as:

```bibtex
@misc{aml-agent-bench,
  author       = {Ejime, Oghenefejiro Macdonald},
  title        = {{AML-Agent-Bench}: A Benchmark for Evidence Traceability in
                   Autonomous AI Agents for Anti-Money Laundering Investigations},
  year         = {2026},
  howpublished = {\url{https://github.com/ejimeoghenefejiro/AML-Agent-Bench}},
  note         = {PhD research codebase, University of Salford}
}
```

## See also

- [README](../README.md) — pull, build and run instructions
- [docs/research-problem.md](research-problem.md) — extended motivation and research gap
- [docs/evidence-traceability-framework.md](evidence-traceability-framework.md) — formal claim–evidence model and failure taxonomy
- [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md) — how reference evidence is annotated
- [docs/validation-plan.md](validation-plan.md) — content/construct/convergent/discriminant validity and reliability plan
- [docs/experimental-design.md](experimental-design.md) — the planned controlled experimental programme
- [docs/research-scope-mapping.md](research-scope-mapping.md) — proposal components vs. current implementation status
- `Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf` — full PhD research proposal
- [tasks/](../tasks/) — current benchmark tasks
