# Experimental Design

> The planned controlled experimental programme for the empirical half of this
> PhD (RQ3, RQ4). See [docs/validation-plan.md](validation-plan.md) for how the
> measurements themselves are validated, and `validation/experiments/README.md`
> for the runner infrastructure this design will actually execute.

## Framing

The empirical study is a controlled, repeated experimental programme, not a leaderboard. The candidate design space is:

```
Models × Agent architectures × Task families × Evidence conditions × Repetitions
```

**Exact sample sizes are not locked in code or documentation yet** — they depend on a power analysis, API/compute cost, and feasibility assessment that has not been done. What follows is the candidate factor structure, not a committed protocol.

## Candidate hypotheses

These are provisional, stated for design purposes, and must not be read as already established by the one-run preliminary results in `docs/preliminary-results.md`.

**H1.** Agents operating under explicit evidence-citation and claim–evidence mapping requirements achieve higher evidence-traceability performance than agents producing unconstrained narrative outputs.

**H2.** Increasing AML task complexity (per the [task complexity taxonomy](research-problem.md#task-complexity-taxonomy)) reduces evidence recall more strongly than evidence precision.

**H3.** Evidence-constrained or retrieval-augmented agents achieve higher claim-support coverage than unconstrained tool-using agents.

**H4.** Task success is positively but imperfectly associated with evidence traceability; therefore conventional task performance does not substitute for traceability evaluation. (The single preliminary Task 006 run — high rubric pass, low traceability recall — is directional feasibility evidence for H4, not a test of it.) **"Task success" here means `outcome_correctness_percentage`** (network reconstruction, typology, innocent-account clearing — see [docs/evidence-traceability-framework.md#outcome-correctness-vs-task-performance](evidence-traceability-framework.md#outcome-correctness-vs-task-performance)), not the full qualitative rubric's `overall_percentage`/`task_performance_percentage`, which itself includes evidence-traceability-flavoured dimensions and would contaminate this comparison if used instead (fix #5).

**H5.** Verifier-assisted architectures reduce invalid references and evidence mismatches relative to single-pass generation.

**H6.** Evidence-oriented interventions improve traceability metrics without a practically significant reduction in AML task performance (again `outcome_correctness_percentage`, not `task_performance_percentage` — an intervention that raises evidence traceability would mechanically raise the full rubric score too, since evidence traceability is one of its dimensions, so using the full rubric here would bias H6 toward appearing true rather than testing it), subject to task complexity and model capability.

## Candidate factors

**Models.** Multiple closed and/or open models, selected based on availability during the empirical study — not fixed in advance.

**Architectures.** Prioritised by relevance to traceability, not generic capability: single tool-using agent (current baseline, `agents/csharp-sk`); retrieval-augmented agent; evidence-constrained agent (explicit citation requirements enforced at the prompt/tool level); verifier-assisted agent (a second pass checks claim–evidence links before the report is finalised). A multi-agent architecture is included only if it is needed to answer a specific RQ, not because an earlier proposal draft mentioned it.

**Conditions.** Unconstrained narrative; citation-required; structured claim–evidence output (**mechanism built, v0.3 item 4** — an agent can opt into `claim_evidence.json` on task-007, and `JudgeAgent.cs`/`StructuredOutputConditionComparison` score and compare it against the narrative-only condition without any LLM step in the claim-to-evidence mapping; see [docs/evidence-traceability-framework.md#structured-citation-output-condition](evidence-traceability-framework.md#structured-citation-output-condition) — no live comparison run has been executed yet, only the machinery to run one); retrieval-constrained; evidence-before-conclusion prompting; verifier-assisted.

**Repetitions.** Repeated runs to estimate variance in non-deterministic agent behaviour, recording model temperature, seed (where supported), runtime, and API/provider metadata. `aml-harness experiment repeat --runs N` (see `validation/experiments/README.md`) is the runner for this; it captures raw per-run measurements only and does not compute or claim a consistency statistic until one is formally defined.

Illustrative experiment matrix: Models × Architectures × Tasks × Conditions × Repeats. The final design may be blocked or fractional to control cost while preserving the contrasts required for RQ3 and RQ4.

## Experimental controls

- Freeze task inputs, gold annotations, and scorer versions per benchmark release.
- Record model name/version, system prompt, agent code commit, tool configuration, dataset hash, and rubric/annotation hash — already substantially implemented in `assurance_profile.json`'s `provenance` block (see `assurance/README.md`).
- Separate deterministic benchmark calculations from stochastic model behaviour (see [LLM-as-judge positioning](evidence-traceability-framework.md#llm-as-judge-positioning)).
- Use the same task assets when comparing models or architectures.
- Predefine exclusion rules for malformed runs and API/tool failures — not yet formalised; `ExperimentRepeatCommand` currently records a run's `error`/`parse_error` field when a nested run's workspace or output can't be parsed, but no exclusion RULE (when does a malformed run get dropped from analysis vs. flagged) has been defined yet.
- Track token use, latency, and cost as secondary efficiency outcomes, never as substitutes for traceability quality. `ExperimentRepeatCommand` already records `latency_seconds` per run; token/cost tracking is not yet captured.

## Statistical analysis plan (placeholder)

The empirical contribution is not reduced to model ranking. Depending on the distributional properties of the resulting metrics (to be examined once real repeated-run data exists), analysis may include:

- descriptive distributions;
- confidence intervals;
- effect sizes;
- repeated-run variance;
- bootstrap intervals where appropriate;
- regression or mixed-effects modelling;
- interactions between model, architecture, task complexity, and intervention (e.g. Architecture × Task Complexity, Intervention × Model);
- multiple-comparison control for broad pairwise testing;
- for H4, explicit quantification of the relationship between task performance and traceability, with discordant cases (high task performance, low traceability, and vice versa) reported directly, not averaged away -- see `validation/gold/discrimination/task-007/{09_correct_outcome_poor_traceability,10_incorrect_outcome_excellent_traceability}.json` (fix #9) for controlled, hand-authored instances of exactly these two discordant cells, with `DiscriminationValidationTests.DiscriminantValidity_Task007_TheTwoFamiliesAreMirrorImagesOfEachOther` asserting the two constructs move in opposite directions across them;
- robustness analysis using alternative gold-evidence formulations, once the [multiple-valid-gold schema](evidence-annotation-protocol.md#multiple-valid-gold-handling) exists to formulate them.

A conceptual mixed-model sketch (illustrative, not a commitment to a specific estimator):

```
Traceability_ijkl = β0 + β1·Model_i + β2·Architecture_j + β3·Complexity_k + β4·Intervention_l + u_task + ε
```

The final model family will be chosen once the actual metric distributions are known, not assumed up front.

## Noise, distractor, and false-positive robustness (RQ3/H2 support)

Starting from `tasks/task-007-multi-source-mule-network`, the plan is controlled variants holding the ground-truth suspicious network constant while varying: no distractors / small / many innocent distractors; irrelevant transactions; irrelevant KYC records; irrelevant graph relationships; incomplete evidence; contradictory evidence — and separately, measuring whether agents over-flag known-innocent entities or under-flag known-suspicious ones. The deterministic half (can the scoring layer itself detect over/under-reporting) is already validated (`tests/AmlAgent.ResearchValidation/DiscriminationValidationTests.cs`). The live half (does an agent actually over/under-report) needs the task variants built and `aml-harness experiment repeat` run against them at a meaningful batch size — see `validation/experiments/README.md` items 10 and 12 for exact status.

## Three-year research programme

Provisional, subject to supervisory review and pilot-data feedback — not a locked commitment.

| Period | Primary work | Key outputs |
|---|---|---|
| Year 1: Construct and instrument | Systematic review; construct definition; task taxonomy; annotation codebook; gold evidence pilot; benchmark refactor; pilot validity tests. | Conceptual paper/protocol; AML-Agent-Bench v0.2 (see [docs/research-scope-mapping.md](research-scope-mapping.md#proposed-version-milestone)); validated annotation procedure; pilot dataset. |
| Year 2: Experimental validation | Scale task families and datasets; multi-annotator validation; cross-model and cross-architecture experiments; construct/discriminant validity; first intervention studies. | Empirical paper 1; benchmark release v0.5; validated traceability profile; comparative results. |
| Year 3: Robustness and synthesis | Adversarial/noisy evidence; intervention comparison; robustness/generalisation analysis; governance mapping; thesis synthesis. | Empirical paper 2; AML-Agent-Bench v1.0; governance mapping; final thesis. |

### Publication strategy

- Paper 1: Conceptualisation and validation of evidence traceability for autonomous AML agents.
- Paper 2: Cross-model and cross-architecture evidence-traceability benchmark study.
- Paper 3 or thesis chapter: Improving traceability through evidence-oriented agent architectures and verification mechanisms.

The publication sequence mirrors the thesis logic: define and validate the measurement instrument first, then use it to produce comparative findings.

## What this document is not

Not a locked protocol, not a pre-registration, not a statement that any of H1–H6 have been tested. It is the design this PhD's empirical chapters will refine once feasibility (cost, API access, task-variant authoring) is assessed. The final hypothesis set itself should be refined after the systematic literature review and pilot studies — H1–H6 above are defensible starting hypotheses, not a closed list.
