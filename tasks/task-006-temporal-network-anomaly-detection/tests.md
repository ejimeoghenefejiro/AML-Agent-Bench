# Task 006 — Test Plan

Outputs are evaluated by **two complementary layers**:

## 1. Structural xUnit tests (deterministic)

Run by `dotnet test` against the workspace once the agent has exited.
Implemented in [Task006SummaryTests.cs](../../tests/AmlAgent.Tests/Task006SummaryTests.cs).

| # | Assertion                                                                                       |
|---|-------------------------------------------------------------------------------------------------|
| 1 | `temporal_anomaly_summary.csv` exists at workspace root                                         |
| 2 | Schema is exactly the 10 columns listed in `prompt.md`                                          |
| 3 | Exactly three rows, with `week` values `week_1`, `week_2`, `week_3` in that order               |
| 4 | All `anomaly_score` values are in `[0, 1]`                                                      |
| 5 | `anomaly_score(week_1) < anomaly_score(week_2) < anomaly_score(week_3)`                         |
| 6 | `week_3.anomaly_score >= 0.7`                                                                   |
| 7 | `total_value`, `anomaly_score` rounded to ≤ 4 decimal places                                    |
| 8 | `temporal_anomaly_report.md` exists, is non-empty, and contains at least 3 `T1-`/`T2-`/`T3-`    |
|   |   transaction-ID citations                                                                       |

These tests fail loudly. They cannot be satisfied by a vague natural-language answer — the agent must produce a correctly-shaped CSV.

## 2. SK-judged qualitative scoring (`rubric.json`)

Run by `aml-agent judge --task task-006-temporal-network-anomaly-detection --workspace <ws>` after the agent exits.

The judge prompt loads:

- `rubric.json` (the rubric below)
- `temporal_anomaly_report.md` (the candidate's report)
- `data/weekly_transfers.csv` (ground-truth data, so the judge can verify citations)

The LLM is asked to produce structured JSON scoring six dimensions on a 0–5 scale:

| dimension                       | 0 = none / wrong                              | 5 = excellent                                    |
|---------------------------------|-----------------------------------------------|--------------------------------------------------|
| `evidence_citation`             | no transaction IDs cited                      | every claim cites a valid txn_id                 |
| `temporal_reasoning`            | treats data as static                         | clear week-over-week comparison                  |
| `anomaly_detection`             | misses the spike                              | correctly identifies week 2/3 anomaly            |
| `fact_vs_assumption`            | states interpretations as facts               | clearly delineates observation vs. interpretation|
| `compliance_tone`               | unhedged, accusatory                          | cautious regulator-friendly language             |
| `avoids_unsupported_claims`     | invents accounts/amounts                      | every claim grounded in the source CSV           |

`overall_percentage` is the sum of scores divided by `30` (the max). `verdict` is `PASS` when `overall_percentage >= pass_threshold_overall` (default `0.7`).

The judge writes `judge_report.json` to the workspace. [JudgeReportTests.cs](../../tests/AmlAgent.Tests/JudgeReportTests.cs) validates the file's shape and that the verdict is `PASS` when the report is present.
