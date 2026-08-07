# Anti-Money Laundering Agent Benchmark: Evaluating Hallucination, Evidence Traceability, Bias, Explainability and Regulatory Trust in Autonomous AI Agents for Financial Crime Detection

> **Thesis abstract.** Canonical, citable form, aligned to the PhD Research
> Proposal (`Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf`). The
> same framing appears in the repository [README](../README.md#abstract).
> When this document and the README diverge, **this file is authoritative**.

---

## Abstract

Financial institutions are rapidly adopting artificial intelligence to support anti-money laundering (AML) and broader financial crime detection. The most recent shift is from static machine-learning classifiers towards autonomous AI agents built on large language models (LLMs) that can reason over multiple steps, call external tools, retrieve evidence, synthesise narratives and draft compliance outputs. These capabilities make agents attractive for suspicious-activity triage, customer risk assessment, typology recognition and case summarisation; the same capabilities also introduce new and poorly understood failure modes — an agent may assert facts the underlying transaction record does not support (hallucination), fail to link its conclusions to traceable evidence, behave inconsistently across customer or jurisdictional groups (bias), produce explanations that do not reflect its actual reasoning, or leave an audit trail inadequate for regulatory review.

General-purpose agent benchmarks such as AgentBench, τ-bench and GAIA evaluate broad capability and tool use, but none addresses the evidentiary, fairness and regulatory demands that govern financial crime detection. This research designs, implements and validates **AML-Agent-Bench**, a domain-specific benchmark for evaluating autonomous AI agents in AML workflows. The primary contribution is a rigorous, operationalised methodology for measuring **hallucination** and **evidence traceability** in agentic AML reasoning, supported by secondary evaluation dimensions covering bias, explainability, auditability and regulatory trust. The work follows a design-science methodology, uses synthetic and public financial-crime datasets (AMLSim, the IBM/NeurIPS synthetic AML transaction data and the Elliptic Bitcoin dataset) to avoid personal data, and grounds its trust criteria in recognised frameworks including the FATF technology guidance, the EU AI Act and supervisory model-risk guidance (SR 11-7). The expected outcome is a reusable open-source benchmark, empirical evidence on where current agents fail in AML contexts, and practical guidance for the responsible evaluation of agentic AI before deployment in regulated environments.

## Six evaluation dimensions

The benchmark evaluates an AML agent along six layered dimensions. The first two are the primary doctoral contribution; the remainder are secondary dimensions assessed through the same harness and reported as a comprehensive risk profile rather than as separate deep studies.

| Dimension | Priority | What it measures | Primary metric |
|---|---|---|---|
| Task performance | Baseline | Correctness of suspicious-activity identification, typology recognition and risk classification | F1 / balanced accuracy |
| Hallucination | **Primary** | Rate of claims unsupported or contradicted by case evidence (intrinsic vs extrinsic) | Evidence-Grounded Hallucination Rate (EGHR) |
| Evidence traceability | **Primary** | Whether each conclusion is correctly linked to the supporting transactions/records | Citation precision / recall |
| Bias and fairness | Secondary | Disparity in flags / risk scores across matched counterfactual customer and jurisdiction profiles | Demographic-parity & equal-opportunity gaps |
| Explainability | Secondary | Quality and faithfulness of generated explanations to the actual decision path | Rubric score + faithfulness check |
| Auditability & trust | Secondary | Completeness of audit logs, alignment to regulatory expectations, uncertainty calibration, run-to-run consistency | Audit-completeness %, ECE, consistency |

## Research questions

**Primary (RQ1).** How can hallucination and evidence traceability in autonomous AML agents be defined, measured reproducibly, and reduced, such that agent conclusions are reliably grounded in the underlying case evidence?

**Supporting:**

- **RQ2** — What forms of bias and explainability failure arise when AI agents are applied to AML tasks, and how can they be measured through counterfactual and faithfulness testing?
- **RQ3** — Which evaluation metrics most validly capture the reliability, auditability and regulatory suitability of AML agents, and how reliable is LLM-as-judge scoring in this domain?
- **RQ4** — How do different agent architectures (single-agent tool use, retrieval-augmented, multi-agent) and underlying models compare across AML task types and risk dimensions?
- **RQ5** — What audit and governance mechanisms, mapped to FATF, EU AI Act and model-risk expectations, are needed to make AML agents acceptable in regulated environments?

## Current prototype

The open-source codebase in this repository is the working foundation the proposal is built on and de-risked by, not yet the finished instrument the proposal describes. It currently implements a C#/.NET 8 agent core on Microsoft Semantic Kernel, a polyglot Docker benchmark harness, two AML tasks (static graph-clustering/risk-scoring and temporal anomaly detection), deterministic xUnit scoring, and an LLM-as-judge that grades a compliance-style report against a six-criterion rubric (evidence citation, temporal reasoning, anomaly detection, fact-vs-assumption separation, compliance tone, absence of unsupported claims).

On top of that rubric, the judge now also computes the proposal's two **primary metrics directly**: an Evidence-Grounded Hallucination Rate (claim extraction with a deterministic citation-existence override, so the LLM cannot mark a fabricated transaction ID as supported) and evidence-traceability citation precision/recall against a curated gold-evidence set — both fully implemented for Task 006 (`src/AmlAgent.Evidence/EvidenceScoring.cs`). Bias/fairness, faithfulness-via-perturbation and audit-completeness/calibration are not yet built. See [docs/dimension-mapping.md](dimension-mapping.md) for the honest, criterion-by-criterion mapping between what is proposed and what is implemented today, and the [provisional timeline](../README.md#1-research-problem) for how the remaining gap closes.

---

**Keywords:** agentic AI · large language models · anti-money laundering · benchmark · hallucination · evidence traceability · bias and fairness · explainability · auditability · regulatory trust · LLM-as-judge · Semantic Kernel · RegTech · FATF · EU AI Act

## Citation

If you reference this work, please cite as:

```bibtex
@misc{aml-agent-bench,
  author       = {Ejime, Oghenefejiro Macdonald},
  title        = {{AML-Agent-Bench}: Evaluating Hallucination, Evidence Traceability,
                   Bias, Explainability and Regulatory Trust in Autonomous AI Agents
                   for Financial Crime Detection},
  year         = {2026},
  howpublished = {\url{https://github.com/ejimeoghenefejiro/AML-Agent-Bench}},
  note         = {PhD research codebase, University of Salford}
}
```

## See also

- [README](../README.md) — pull, build and run instructions
- [docs/research-problem.md](research-problem.md) — extended motivation and research gap
- [docs/dimension-mapping.md](dimension-mapping.md) — proposal dimensions vs. current implementation status
- `Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf` — full PhD research proposal
- [tasks/](../tasks/) — current benchmark tasks
