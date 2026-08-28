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

**H4.** Task success is positively but imperfectly associated with evidence traceability; therefore conventional task performance does not substitute for traceability evaluation. (The single preliminary Task 006 run — high rubric pass, low traceability recall — is directional feasibility evidence for H4, not a test of it.)

## Candidate factors

**Models.** Multiple closed and/or open models, selected based on availability during the empirical study — not fixed in advance.

**Architectures.** Prioritised by relevance to traceability, not generic capability: single tool-using agent (current baseline, `agents/csharp-sk`); retrieval-augmented agent; evidence-constrained agent (explicit citation requirements enforced at the prompt/tool level); verifier-assisted agent (a second pass checks claim–evidence links before the report is finalised). A multi-agent architecture is included only if it is needed to answer a specific RQ, not because an earlier proposal draft mentioned it.

**Conditions.** Unconstrained narrative; citation-required; structured claim–evidence output; retrieval-constrained; evidence-before-conclusion prompting; verifier-assisted.

**Repetitions.** Repeated runs to estimate variance in non-deterministic agent behaviour, recording model temperature, seed (where supported), runtime, and API/provider metadata. `aml-harness experiment repeat --runs N` (see `validation/experiments/README.md`) is the runner for this; it captures raw per-run measurements only and does not compute or claim a consistency statistic until one is formally defined.

## Statistical analysis plan (placeholder)

The empirical contribution is not reduced to model ranking. Depending on the distributional properties of the resulting metrics (to be examined once real repeated-run data exists), analysis may include:

- descriptive distributions;
- confidence intervals;
- effect sizes;
- repeated-run variance;
- bootstrap intervals where appropriate;
- regression or mixed-effects modelling;
- interactions between model, architecture, task complexity, and intervention;
- multiple-comparison control for broad pairwise testing.

A conceptual mixed-model sketch (illustrative, not a commitment to a specific estimator):

```
Traceability_ijkl = β0 + β1·Model_i + β2·Architecture_j + β3·Complexity_k + β4·Intervention_l + u_task + ε
```

The final model family will be chosen once the actual metric distributions are known, not assumed up front.

## Noise, distractor, and false-positive robustness (RQ3/H2 support)

Starting from `tasks/task-007-multi-source-mule-network`, the plan is controlled variants holding the ground-truth suspicious network constant while varying: no distractors / small / many innocent distractors; irrelevant transactions; irrelevant KYC records; irrelevant graph relationships; incomplete evidence; contradictory evidence — and separately, measuring whether agents over-flag known-innocent entities or under-flag known-suspicious ones. The deterministic half (can the scoring layer itself detect over/under-reporting) is already validated (`tests/AmlAgent.ResearchValidation/DiscriminationValidationTests.cs`). The live half (does an agent actually over/under-report) needs the task variants built and `aml-harness experiment repeat` run against them at a meaningful batch size — see `validation/experiments/README.md` items 10 and 12 for exact status.

## What this document is not

Not a locked protocol, not a pre-registration, not a statement that any of H1–H4 have been tested. It is the design this PhD's empirical chapters will refine once feasibility (cost, API access, task-variant authoring) is assessed.
