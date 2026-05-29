# Preliminary results

First live cross-model runs on Task 006 (`task-006-temporal-network-anomaly-detection`).

**Date:** 2026-05-14
**Bench commit:** see `git log -1`
**Agent:** `agents/csharp-sk/` — C# / Semantic Kernel reference agent
**Provider:** OpenAI (organization Tier-1 limits)
**Workspace seed:** identical for every run; staged from `tasks/task-006-temporal-network-anomaly-detection/`

> These are **first-data** results from real, autonomous agent runs against the
> bench framework, not smoke tests against hand-crafted submissions. They are
> intended to evidence that the bench (a) functions end-to-end against live
> LLMs and (b) discriminates meaningfully between model behaviours. They are
> **not** a controlled comparative study — that work is the first empirical
> objective of the PhD.

## Headline

| Model | Agent runtime | xUnit | LLM-judge | Outcome |
|---|---|---|---|---|
| `gpt-4o-mini` | **12.1 s** | **15 / 15 PASS** | **29 / 30 = 96.7 % PASS** | Complete, both outputs produced |
| `gpt-4o` (Tier-1) | 86.5 s, throttled | FAIL `AnomalyScoresInRange` | n/a — no markdown report produced | Incomplete due to TPM rate-limit |

## Detail — gpt-4o-mini

`BENCH_MAX_STEPS=12`, model authored both outputs in a single tool-calling round, replied `DONE`, exited cleanly in 12.1 s.

**`temporal_anomaly_summary.csv`:**

```text
week,start_date,end_date,transfer_count,unique_accounts,total_value,new_accounts_count,high_risk_dest_count,sar_count,anomaly_score
week_1,2026-01-01,2026-01-07,10,5,31800,5,0,0,0.0000
week_2,2026-01-08,2026-01-14,15,8,195000,3,0,5,0.5556
week_3,2026-01-15,2026-01-21,6,6,339000,3,3,3,1.0000
```

Observations:

- Schema correct.
- Week labels correct (`week_1`, `week_2`, `week_3`).
- Week boundaries shifted by 4 days (used ISO week boundary instead of `2026-01-05` from the prompt).
- Undercounted SAR-linked transfers (5 vs 7 in week 2; 3 vs 6 in week 3).
- Undercounted high-risk destinations (3 vs 6 in week 3).
- Monotone increasing anomaly score, week 3 = 1.0000 — passes the strictly-increasing and `>= 0.7` tests.

**LLM-judge per-dimension scores:**

| Dimension | Score | Judge reasoning |
|---|---|---|
| `evidence_citation` | 5 / 5 | "All claims are supported by valid transaction IDs from the provided data." |
| `temporal_reasoning` | 5 / 5 | "The analysis effectively discusses changes in transaction patterns over the three-week period." |
| `anomaly_detection` | 5 / 5 | "Correctly identifies week 2 as the start of anomalous activity and week 3 as the exit phase, noting key indicators." |
| `fact_vs_assumption` | 4 / 5 | "Mostly separates facts from assumptions, but could clarify further on implications of the data." |
| `compliance_tone` | 5 / 5 | "Uses cautious language and avoids direct accusations of wrongdoing." |
| `avoids_unsupported_claims` | 5 / 5 | "No unsupported claims are made; all cited data is accurate and verifiable." |
| **Overall** | **29 / 30 = 96.7 %** | **PASS** (threshold 70 %) |

## Detail — gpt-4o

`BENCH_MAX_STEPS=8`. Agent wrote a more sophisticated 8 KB analysis script (correct ISO-8601 timestamp parsing, hard-coded week boundaries matching the prompt exactly, planned `GenerateReportMarkdown` step) but hit the 30 000 TPM Tier-1 rate-limit before completing the second output. **86.5 s elapsed; one OpenAI 429 response.**

**`temporal_anomaly_summary.csv` (partial):**

```text
week,start_date,end_date,transfer_count,unique_accounts,total_value,new_accounts_count,high_risk_dest_count,sar_count,anomaly_score
week_1,2026-01-05,2026-01-11,9,5,38700.0000,5,0,0,0.0000
week_2,2026-01-12,2026-01-18,14,8,426800.0000,3,0,6,2.9196
week_3,2026-01-19,2026-01-25,6,6,353000.0000,5,6,6,3.4425
```

Observations:

- **More accurate** than gpt-4o-mini on dates (correct `2026-01-05` start), SAR counts (6 vs 6, 6 vs 3), and high-risk destinations (6 vs 6).
- **Failed to normalise `anomaly_score` to `[0, 1]`** — values 2.9196 and 3.4425 break the xUnit `AnomalyScoresInRange` assertion.
- No `temporal_anomaly_report.md` produced — the agent was rate-limited before the second output step, so the LLM-judge stage was not applicable.

**xUnit verdict:** FAIL on `Task006SummaryTests.AnomalyScoresInRange`.

## What this preliminary data already tells us

1. **The bench framework works end-to-end against live LLMs.** Both the autonomous `run` loop and the LLM-as-judge produce structured outputs on real model traffic.
2. **The framework discriminates between models in a non-trivial way.** The "cheaper" model passed; the "stronger" model failed on a normalisation rule it forgot. This is the kind of qualitative finding that motivates the PhD's empirical objective.
3. **Operational friction is real and measurable.** OpenAI Tier-1 TPM (30 000 for gpt-4o, 200 000 for gpt-4o-mini) is the binding constraint on iteration speed for the stronger model. The harness already produces clean 429 traces — a useful side-result for any RegTech buyer considering production deployment.
4. **The LLM-judge does not catch numeric errors that xUnit catches.** gpt-4o-mini's CSV had two undercount errors (SAR and high-risk-destination counts) that xUnit doesn't currently assert, and the LLM judge — looking at the markdown report — scored evidence-citation 5 / 5. This validates the **dual-evaluator design**: structural correctness and qualitative compliance writing are genuinely different signals.

## Next empirical steps

These results are first-data, not a controlled study. The PhD's first-year empirical objective is to extend this into:

- Multiple seeds per (model, task) pair.
- A broader model family sweep (Claude, Gemini, Llama, open-weights, plus tier-2 gpt-4o).
- Variance and rank-correlation analysis between xUnit and LLM-judge.
- Sensitivity analysis of the LLM-judge rubric and judge model.
- Reproducibility tracking via per-call temperature seeding.

A small `eval/` harness wrapping `aml-harness` for sweep execution and results aggregation is the natural next code artefact.
