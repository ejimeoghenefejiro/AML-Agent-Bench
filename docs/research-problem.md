# Research Problem

> Aligned to `Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf`. See that
> document for the full literature review, methodology and timeline; this page
> is the working summary linked from the README.

## Core claim

Financial institutions increasingly use AI to support AML and financial-crime investigation. Emerging autonomous agents differ from conventional classifiers because they may retrieve records, call analytical tools, analyse transaction networks, reason over multiple steps, and generate investigative narratives or recommendations.

Conventional AML evaluation typically focuses on predictive or task performance. General-purpose agent benchmarks such as AgentBench, τ-bench and GAIA typically focus on capability, task completion, reasoning, or tool use. Neither establishes whether the evidentiary basis of an agent's conclusion can be independently reconstructed and verified.

## Why this matters

An autonomous AML agent may:

- reach a correct-looking conclusion while citing weak evidence;
- cite a real transaction that is irrelevant to the claim;
- identify only part of the material evidence;
- omit critical records;
- make a claim without any identifiable supporting evidence;
- provide an evidence chain that another analyst cannot reproduce.

AML compliance is high-stakes and error-sensitive: a false positive delays legitimate transactions and inflates cost; a false negative lets illicit funds move undetected. An unsupported or untraceable claim in a suspicious-activity narrative can mislead an investigation, contaminate a regulatory filing, and create legal exposure — regardless of whether the agent's overall conclusion happens to be correct. FATF (2021), the EU AI Act (2024) and supervisory model-risk guidance (SR 11-7, 2011) all require that high-impact financial-system outputs remain auditable and traceable to their evidentiary basis; existing agent benchmarks contain no AML tasks and no evidentiary-traceability scoring at all.

## Research problem

> **How can evidence traceability in autonomous AML-agent investigations be rigorously defined, operationalised, measured, and validated?**

## Research gap

Existing citation, attribution, grounding, and general-purpose agent-evaluation methods have not been systematically operationalised and empirically validated as an evidence-traceability benchmark for autonomous AML investigations over structured financial records and multi-step investigative tasks. This is not a claim that no citation or grounding evaluation exists — it is a narrower claim about four specific layers where that work has not yet been done for this domain:

**Domain gap.** Existing citation and attribution evaluation is often applied to document retrieval, question answering, search, or text generation. AML-Agent-Bench evaluates evidence such as transaction IDs, account relationships, temporal movement, transaction-network patterns, typology indicators, risk-relevant records, and evidence packages supporting investigative claims — evidence types with no direct analogue in general-purpose grounding benchmarks.

**Agentic gap.** The subject being evaluated is not merely a text generator. An autonomous AML agent may inspect files or data, call tools, perform calculations, build graph or temporal representations, retrieve evidence, derive a conclusion, and generate an investigative output. Evidence traceability should therefore evaluate the relationship between agent claims, cited evidence, underlying records, and (where appropriate) the recorded execution trajectory — not just the final text.

**Measurement gap.** A single citation count or holistic LLM rubric is insufficient. The benchmark distinguishes citation/reference validity, evidence relevance, evidence precision, evidence recall, claim-support coverage, evidence sufficiency, traceability failure type, and reproducibility of the evidence mapping.

**Validation gap.** The main doctoral contribution is not inventing another F1 variant — it is the construction and validation of a domain-specific measurement framework, ultimately demonstrating content validity, construct validity, convergent validity, discriminant validity, reliability, reproducibility, and sensitivity to controlled traceability degradation. See [docs/validation-plan.md](validation-plan.md).

## Proposed contribution

AML-Agent-Bench evaluates an AML agent's evidence traceability as the sole primary doctoral construct. Task performance, reproducibility, auditability, human review, and governance are supporting properties measured through the same harness, not separate deep studies competing for primacy. See [docs/evidence-traceability-framework.md](evidence-traceability-framework.md) for the formal claim–evidence model and [docs/research-scope-mapping.md](research-scope-mapping.md) for what the current codebase implements today versus what remains.

## Research questions

**RQ1 — Conceptualisation.** How should evidence traceability in autonomous AML-agent investigations be conceptualised and operationalised at claim and evidence level?

**RQ2 — Measurement and validation.** To what extent can evidence traceability be measured reliably using claim-level evidence validity, precision, recall, coverage, and sufficiency measures against validated reference evidence?

**RQ3 — Empirical variation.** How does evidence-traceability performance vary across underlying language models, agent architectures, AML task types, and task-complexity levels?

**RQ4 — Improvement interventions.** Which agent-design interventions improve evidence traceability without materially degrading AML task performance? Candidate interventions include explicit citation requirements, structured evidence fields, evidence-before-conclusion prompting, retrieval-constrained generation, mandatory claim–evidence mapping, verification/review agents, evidence-aware tool design, and evidence-selection constraints.

## Task complexity taxonomy

The task suite is organised by evidence-traceability complexity rather than by which trust dimension a task was meant to exercise. The current prototype implements the first two levels:

| Level | Task family | What it measures | Status |
|---|---|---|---|
| 1 — Direct evidence retrieval | Agent identifies a suspicious fact/pattern and cites a directly supporting record | Basic reference validity and precision | **Implemented** — `tasks/aml-transaction-network` |
| 2 — Multi-record aggregation / temporal reasoning | Agent combines several records, or evidence across time windows, to support one conclusion | Completeness and sufficiency | **Implemented** — `tasks/task-006-temporal-network-anomaly-detection` |
| 3 — Network reasoning | Agent establishes a relational pattern (circular flow, layering, connected suspicious clusters) across multiple heterogeneous sources | Relational evidence chains, cross-source traceability | **Implemented** — `tasks/task-007-multi-source-mule-network` |
| 4 — Case synthesis | Agent produces a multi-claim investigative case summary or SAR-style reasoning output with claim-specific evidence packages | Claim-level evidence mapping at case scale | Planned |
| 5 — Ambiguous/adversarial evidence | Controlled difficulty: irrelevant distractor transactions, near-duplicate IDs, incomplete records, competing plausible explanations, conflicting evidence, missing evidence, noisy data | How traceability degrades with complexity | Planned — see `validation/experiments/README.md` items 10–12 for the noise/distractor and false-positive-protection groundwork already in place |

## Datasets

The current tasks use small, hand-authored synthetic CSV/JSON/Parquet datasets (see `scripts/generate_synthetic_aml_data.py` and each task's `environment/data/`), chosen for determinism and zero licensing/PII risk while the harness, adapter layer, and metrics were being built. The proposal's later phase extends this to public research datasets — AMLSim (Suzumura and Kanezashi, 2021), the NeurIPS synthetic AML transaction data (Altman et al., 2023) and the Elliptic Bitcoin dataset (Weber et al., 2019) — from which controlled cases with known ground-truth evidence sets will be authored so evidence traceability can be scored against a complete reference at greater scale.

## Expected contribution to PhD

1. **Conceptual** — a domain-specific formalisation and taxonomy of evidence traceability and traceability failure in autonomous AML investigations.
2. **Methodological** — a validated claim–evidence measurement framework for assessing traceability against annotated reference evidence.
3. **Empirical** — evidence on how traceability varies across models, architectures, AML task complexity, and evidence-oriented interventions.
4. **Technical** — AML-Agent-Bench: an open and reproducible benchmark implementation for executing and evaluating autonomous AML agents under controlled evidentiary conditions.

See `Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf` for the full literature review, phased methodology, risk register and provisional timeline.
