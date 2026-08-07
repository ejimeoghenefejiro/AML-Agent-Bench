# Research Problem

> Aligned to `Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf`. See that
> document for the full literature review, methodology and timeline; this page
> is the working summary linked from the README.

## Core claim

We currently lack a rigorous, domain-appropriate way to determine whether an
autonomous AI agent is trustworthy enough to support AML decision-making.
General-purpose agent benchmarks such as AgentBench, τ-bench and GAIA
evaluate broad capability and tool use, but none addresses the evidentiary,
fairness and regulatory demands that govern financial crime detection.

## Why this matters

AML compliance is high-stakes and error-sensitive: a false positive delays
legitimate transactions and inflates cost; a false negative lets illicit
funds move undetected. Traditional rule-based and ML transaction monitoring
already has well-documented false-positive rates in the 90-98% range and
offers limited, hard-to-audit explanations (Chen et al., 2018). Autonomous
LLM agents are a qualitatively different paradigm — they can plan, call
tools, retrieve and synthesise evidence, and draft narratives — and they
introduce risks that classification metrics do not capture:

- **Hallucination** — LLM agents are known to generate fluent but unsupported
  claims (Ji et al., 2023; Huang et al., 2023). In AML, an unsupported
  assertion in a suspicious-activity narrative can mislead an investigation,
  contaminate a regulatory filing and create legal exposure.
- **Evidence traceability** is mandatory but unmeasured — every conclusion
  should be traceable to specific transactions, records or rules, but agent
  benchmarks reward task success, not whether each claim is grounded.
- **Bias, explainability and auditability** are regulatory expectations, not
  optional extras: FATF (2021), the EU AI Act (2024) and supervisory
  model-risk guidance (SR 11-7, 2011) require fairness, transparency,
  documentation and human oversight for high-impact financial systems.
- **Existing benchmarks are domain-agnostic.** AgentBench, τ-bench and GAIA
  contain no AML tasks, no evidentiary scoring and no regulatory-trust
  dimension.

## Research gap

Three literatures — AML machine learning, agent evaluation, and
trustworthy/responsible AI — remain largely disconnected. AML datasets and
typologies exist but are used to score classifiers, not agents. Agent
benchmarks exist but are domain-agnostic and ignore evidence and regulation.
Trustworthy-AI taxonomies (hallucination, XAI, fairness) and regulatory
frameworks exist but have not been operationalised into measurable tests for
AML agents. **The gap is the absence of a domain-specific,
regulation-aligned benchmark that evaluates autonomous AI agents on AML
tasks with operational metrics for hallucination, evidence traceability,
bias, explainability and auditability.**

## Proposed contribution

AML-Agent-Bench evaluates an AML agent along six layered dimensions —
task performance (baseline), hallucination and evidence traceability
(**primary**), and bias/fairness, explainability, auditability & trust
(secondary). See [docs/abstract.md](abstract.md#six-evaluation-dimensions)
for the full dimension table and metrics.

The doctoral contribution is deliberately concentrated on hallucination and
evidence traceability in agentic AML reasoning — the dimension most
decision-critical in a regulated, evidentiary domain and least addressed by
existing benchmarks — while the secondary dimensions are evaluated through
the same harness and reported as a comprehensive risk profile rather than
exhaustively studied. See [docs/dimension-mapping.md](dimension-mapping.md)
for what the current codebase implements today versus what each phase of
the proposal adds.

## Research questions

**Primary (RQ1).** How can hallucination and evidence traceability in
autonomous AML agents be defined, measured reproducibly, and reduced, such
that agent conclusions are reliably grounded in the underlying case
evidence?

**Supporting:**

1. **RQ2** — What forms of bias and explainability failure arise when AI
   agents are applied to AML tasks, and how can they be measured through
   counterfactual and faithfulness testing?
2. **RQ3** — Which evaluation metrics most validly capture the reliability,
   auditability and regulatory suitability of AML agents, and how reliable
   is LLM-as-judge scoring in this domain?
3. **RQ4** — How do different agent architectures (single-agent tool use,
   retrieval-augmented, multi-agent) and underlying models compare across
   AML task types and risk dimensions?
4. **RQ5** — What audit and governance mechanisms, mapped to FATF, EU AI Act
   and model-risk expectations, are needed to make AML agents acceptable in
   regulated environments?

## Candidate task families

The proposal's task taxonomy (Phase 2) covers six families; the current
prototype implements the first two:

| # | Task family | Status |
|---|---|---|
| 1 | Suspicious-transaction and red-flag identification | **Implemented** — `tasks/aml-transaction-network` |
| 2 | Transaction-network and typology analysis (structuring, layering, rapid movement) | **Implemented** — `tasks/task-006-temporal-network-anomaly-detection` |
| 3 | Customer risk-profile assessment and counterfactual fairness probes | Planned (Phase 2/6 — feeds RQ2 bias testing) |
| 4 | Evidence-based case summarisation and SAR-reasoning | Planned |
| 5 | Regulatory explanation generation and uncertainty recognition | Planned |
| 6 | Adversarial/robustness tasks with incomplete or noisy data and injected distractors | Planned |

## Datasets

The two current tasks use small, hand-authored synthetic CSVs (see
`scripts/generate_synthetic_aml_data.py` and
`tasks/task-006-temporal-network-anomaly-detection/environment/data/`),
chosen for determinism and zero licensing/PII risk while the harness and
metrics were being built. The proposal's Phase 3 extends this to public
research datasets — AMLSim (Suzumura and Kanezashi, 2021), the NeurIPS
synthetic AML transaction data (Altman et al., 2023) and the Elliptic
Bitcoin dataset (Weber et al., 2019) — from which controlled cases with
known ground-truth evidence sets will be authored so hallucination and
traceability can be scored against a complete reference.

## Expected contribution to PhD

1. A domain-specific, regulation-aligned benchmark framework for evaluating
   autonomous AI agents in AML workflows.
2. Novel operational metrics for evidence-grounded hallucination (EGHR) and
   evidence traceability in agentic financial-crime reasoning.
3. Empirical evidence on the failure modes of current agent architectures
   and models across AML tasks and risk dimensions.
4. An open-source benchmark system (AML-Agent-Bench) reusable by
   researchers and industry.
5. A mapping from benchmark evidence to regulatory expectations (FATF, EU AI
   Act, SR 11-7), and practical guidance for governing AML agents
   responsibly.

See `Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf` for the full
literature review, phased methodology (Phases 1-7), risk register and
provisional three-year timeline.
