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

AML compliance is high-stakes and error-sensitive: a false positive delays legitimate transactions and inflates cost; a false negative lets illicit funds move undetected. An unsupported or untraceable claim in a suspicious-activity narrative can mislead an investigation, contaminate a regulatory filing, and create legal exposure — regardless of whether the agent's overall conclusion happens to be correct. FATF (2021), the EU AI Act (2024) and supervisory model-risk guidance (SR 11-7, 2011) each impose documentation, record-keeping, auditability, and human-oversight requirements on high-risk or automated financial-system outputs; none of them mandate "evidence traceability" by that name or as a specific technical construct, but the recurring expectation across all three is that a flagged transaction or automated decision can be explained, reviewed, and traced back to what supports it. Evidence traceability, as defined and operationalised in this research, is one way to make that broader documentation/auditability/human-oversight expectation empirically measurable for autonomous agents specifically — not a claim that these instruments impose this exact requirement. Among the general-purpose and AML-oriented agent benchmarks reviewed for this research (see the literature review in the PhD proposal, and AgentBench/τ-bench/GAIA above), none combine AML-specific tasks with evidentiary-traceability scoring — a gap in the literature surveyed, not an assertion that no such benchmark exists anywhere.

## Research problem

> **There is no established, validated and domain-specific method for determining how well the material claims produced by autonomous AML agents can be traced to identifiable, relevant and sufficient evidence in the underlying investigation record.**

## Why the research had to be narrowed

The original framing combined hallucination, evidence traceability, bias/fairness, explainability, auditability, and regulatory trust as joint dimensions. Each has distinct theory, measurement challenges, datasets, and validation requirements; operationalising all of them at doctoral depth risked a thesis that was broad but methodologically shallow.

| Original dimension | Why it expands scope | Revised treatment |
|---|---|---|
| Hallucination | Requires claim taxonomies, semantic support/contradiction judgments, and validation of what counts as intrinsic vs. extrinsic hallucination. | Removed as a primary construct. Unsupported, contradicted, or fabricated evidence is treated as a [traceability failure class](evidence-traceability-framework.md#traceability-failure-taxonomy), not a separate hallucination theory. |
| Bias/fairness | Requires protected-group design, counterfactual cases, fairness definitions, statistical power, and potentially sensitive data. | Removed from core doctoral scope. |
| Explainability | Requires a theory of explanation quality and faithfulness, often including perturbation or causal tests. | No separate XAI contribution claimed. Retained only where needed to interpret evidence links. |
| Auditability | Can become a major systems-governance programme in its own right. | Treated as a supporting property of reproducibility and evidence reconstruction, not a measured dependent variable. |
| Regulatory trust | Trust is a human and institutional construct, difficult to reduce to an automatic benchmark score. | Reframed as downstream governance relevance (see [assurance/README.md](../assurance/README.md#positioning-relative-to-the-phd)), not a measured dependent variable. |
| Evidence traceability | Directly aligned with transaction records, claim support, reproducibility, and evidentiary AML workflows. | Made the sole core doctoral construct. |

## Research gap

Existing citation, attribution, grounding, and general-purpose agent-evaluation methods have not been systematically operationalised and empirically validated as an evidence-traceability benchmark for autonomous AML investigations over structured financial records and multi-step investigative tasks. This is not a claim that no citation or grounding evaluation exists — it is a narrower claim about five specific layers where that work has not yet been done for this domain:

| Gap layer | Specific gap | AML-Agent-Bench response |
|---|---|---|
| Domain gap | Existing citation/attribution evaluation is oriented to document retrieval, question answering, or web/source citation. | Evaluate claims grounded in transaction IDs, account relations, temporal patterns, network structures, typologies, and case evidence — evidence types with no direct analogue in general-purpose grounding benchmarks. |
| Agentic gap | Final-answer citation quality does not fully capture tool use, retrieval trajectories, and multi-step investigation. | Evaluate evidence links generated within autonomous, tool-using AML workflows — the relationship between agent claims, cited evidence, underlying records, and (where appropriate) the recorded execution trajectory, not just the final text. |
| Measurement gap | A single citation count or holistic LLM rubric is insufficient; a score is not useful unless its construct validity, reliability, and sensitivity are established. | Develop and validate a multi-component evidence-traceability measurement model (see [docs/evidence-traceability-framework.md](evidence-traceability-framework.md)) distinguishing reference validity, precision, recall, coverage, sufficiency, and reconstruction. |
| Benchmark gap | AML evaluation typically emphasises detection/prediction rather than evidentiary reconstruction. | Create controlled AML scenarios with gold claim-evidence mappings (see [docs/evidence-annotation-protocol.md](evidence-annotation-protocol.md)). |
| Intervention gap | It is not enough to diagnose weak traceability. | Test evidence-oriented agent designs that attempt to improve traceability without sacrificing task performance (RQ4; see [docs/experimental-design.md](experimental-design.md)). |

The doctoral novelty is the framework and benchmark together with their empirical validation, not the invention of precision, recall, or F1 — see [docs/validation-plan.md](validation-plan.md) for how content validity, construct validity, convergent validity, discriminant validity, reliability, reproducibility, and sensitivity to controlled traceability degradation are each demonstrated (or honestly marked as not yet demonstrated).

## Proposed contribution

AML-Agent-Bench evaluates an AML agent's evidence traceability as the sole primary doctoral construct. Task performance, reproducibility, auditability, human review, and governance are supporting properties measured through the same harness, not separate deep studies competing for primacy. See [docs/evidence-traceability-framework.md](evidence-traceability-framework.md) for the formal claim–evidence model and [docs/research-scope-mapping.md](research-scope-mapping.md) for what the current codebase implements today versus what remains.

## Objectives

1. Conceptualise evidence traceability in autonomous AML investigation and define its constituent properties.
2. Develop an AML-specific claim-evidence annotation framework and benchmark task taxonomy.
3. Operationalise evidence traceability using deterministic and, where necessary, validated semantic measures.
4. Establish the reliability and validity of the benchmark and its gold evidence annotations.
5. Experimentally compare evidence-traceability performance across models, agent architectures, task complexity levels, and evidence conditions.
6. Evaluate interventions designed to improve evidence traceability without materially degrading AML task performance.
7. Release a reproducible research artefact and document how its outputs can support human review and model governance.

## Research questions

**RQ1 — Conceptualisation.** How should evidence traceability in autonomous AML-agent investigations be conceptualised and operationalised at claim and evidence level?

**RQ2 — Measurement and validation.** To what extent can evidence traceability be measured reliably using claim-level evidence validity, precision, recall, coverage, and sufficiency measures against validated reference evidence?

**RQ3 — Empirical variation.** How does evidence-traceability performance vary across underlying language models, agent architectures, AML task types, and task-complexity levels?

**RQ4 — Improvement interventions.** Which agent-design interventions improve evidence traceability without materially degrading AML task performance? Candidate interventions include explicit citation requirements, structured evidence fields, evidence-before-conclusion prompting, retrieval-constrained generation, mandatory claim–evidence mapping, verification/review agents, evidence-aware tool design, and evidence-selection constraints.

## Task complexity taxonomy

The task suite is organised by evidence-traceability complexity rather than by which trust dimension a task was meant to exercise:

| Level | Task family | Traceability challenge | Status |
|---|---|---|---|
| 1 — Direct evidence retrieval | Single claim to single evidence item — agent identifies a suspicious fact/pattern and cites a directly supporting record | Basic reference validity and precision | **Implemented** — `tasks/aml-transaction-network` |
| 2 — Multi-record aggregation | Single claim requires several evidence items (e.g. demonstrating structuring across multiple transfers) | Completeness and sufficiency within one time window | Planned as a standalone task — `tasks/task-006-temporal-network-anomaly-detection` already exercises this challenge *within* each week as part of its temporal-reasoning task (level 4 below), but no task isolates multi-record aggregation on its own yet |
| 3 — Network reasoning | Claims depend on relational graph structure across multiple heterogeneous sources (circular flow, layering, connected suspicious clusters) | Relational evidence chains, cross-source traceability | **Implemented** — `tasks/task-007-multi-source-mule-network` |
| 4 — Temporal reasoning | Claims depend on changes across time windows (week-over-week change, rapid movement) | Completeness and sufficiency across time | **Implemented** — `tasks/task-006-temporal-network-anomaly-detection` |
| 5 — Case synthesis | Multiple claims require separate evidence packages — agent produces a multi-claim investigative case summary or SAR-style reasoning output | Claim-level evidence mapping at case scale | Planned |
| 6 — Ambiguous/adversarial evidence | Noisy, distracting, or incomplete records challenge evidence selection: near-duplicate IDs, irrelevant high-value transfers, partial records, conflicting clues | How traceability degrades with complexity | Planned — see `validation/experiments/README.md` items 10–12 for the noise/distractor and false-positive-protection groundwork already in place |

## Datasets

The current tasks use small, hand-authored synthetic CSV/JSON/Parquet datasets (see `scripts/generate_synthetic_aml_data.py` and each task's `environment/data/`), chosen for determinism and zero licensing/PII risk while the harness, adapter layer, and metrics were being built. The proposal's later phase extends this to public research datasets — AMLSim (Suzumura and Kanezashi, 2021), the NeurIPS synthetic AML transaction data (Altman et al., 2023) and the Elliptic Bitcoin dataset (Weber et al., 2019) — from which controlled cases with known ground-truth evidence sets will be authored so evidence traceability can be scored against a complete reference at greater scale.

## Expected contribution to PhD

1. **C1 — Conceptual** — a formal definition and taxonomy of evidence traceability and traceability failure modes for autonomous AML investigations.
2. **C2 — Methodological** — a claim-level measurement framework covering evidence validity, precision, recall, coverage, sufficiency, and reconstruction, together with its empirical validation (see [docs/validation-plan.md](validation-plan.md)) — validation is planned work, not yet performed; see [docs/research-scope-mapping.md](research-scope-mapping.md) for current status.
3. **C3 — Benchmark/data** — a reproducible family of AML investigative tasks with expert-annotated claim-evidence reference structures.
4. **C4 — Empirical** — evidence on how model choice, architecture, task complexity, and evidence conditions affect traceability.
5. **C5 — Intervention** — experimental evidence on which agent-design interventions improve traceability while preserving task performance.
6. **C6 — Technical** — AML-Agent-Bench as an open, reusable research implementation with deterministic scoring, versioned artefacts, and reproducibility controls.
7. **C7 — Applied** — a disciplined mapping of traceability outputs to human-review and model-governance use cases, without claiming autonomous regulatory certification.

The thesis does not present Evidence Traceability F1 itself as the principal novelty — precision, recall, and F1 are established measures. Novelty arises from the domain-specific construct, operational framework, reference annotations, validation methodology, benchmark design, and empirical findings.

## Risks, limitations and mitigations

| Risk | Why it matters | Mitigation |
|---|---|---|
| Gold evidence subjectivity | Some AML conclusions admit multiple defensible evidence sets. | Represent alternative acceptable sets (see [planned claim-level schema](research-scope-mapping.md#planned-claim-level-schema)); use expert annotation and adjudication; report ambiguity explicitly rather than silently picking one set. |
| Toy-data validity | Small synthetic tasks may not represent realistic investigations. | Use synthetic data for controlled ground truth first, then expand to larger public/synthetic datasets and increasingly realistic cases (see [Datasets](#datasets) below). |
| Model drift/versioning | Commercial model behaviour changes over time. | Record exact model/version/date in every run's provenance (already implemented — see `assurance/README.md`); preserve benchmark artefacts; scope conclusions to the observed configuration. |
| LLM judge bias | Semantic scoring can introduce opaque evaluator error. | Prefer deterministic scoring wherever possible (see [LLM-as-judge positioning](evidence-traceability-framework.md#llm-as-judge-positioning)); human-validate unavoidable semantic judgments once real annotations exist. |
| Benchmark gaming | Agents may optimise for citation patterns without genuine evidence use. | Include hidden or held-out tasks, perturbations, sufficiency checks, and evidence-order variations — the evidence-corruption sensitivity tests (`tests/AmlAgent.ResearchValidation/EvidenceCorruptionSensitivityTests.cs`) are early groundwork here, though they test the scoring layer's sensitivity, not yet agent gaming behaviour directly. |
| Overclaiming regulation | A benchmark cannot determine legal compliance. | State governance relevance narrowly (see [assurance/README.md](../assurance/README.md#positioning-relative-to-the-phd)) and keep human/institutional judgment explicit. |
| Scope creep | Re-adding fairness, XAI, or trust as measured constructs would recreate the original scope problem. | Maintain evidence traceability as the thesis boundary (see [Why the research had to be narrowed](#why-the-research-had-to-be-narrowed) above); treat other concerns as future research. |

See `Proposal/Oghenefejiro Ejime - PhD Research Proposal.pdf` for the full literature review, phased methodology, risk register and provisional timeline.
