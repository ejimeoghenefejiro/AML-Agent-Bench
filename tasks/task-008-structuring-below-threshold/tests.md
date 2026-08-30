# Task 008 — Test Plan

Outputs are evaluated by **two complementary layers**, same model as task-006/task-007.

## 1. Structural xUnit tests (deterministic)

Run by `dotnet test` against the workspace once the agent has exited.
Implemented in [Task008StructuringFindingsTests.cs](../../tests/AmlAgent.Tests/Task008StructuringFindingsTests.cs).

| # | Assertion |
|---|-----------|
| 1 | `structuring_findings.csv` exists at workspace root |
| 2 | Schema is exactly `txn_id,classification,amount,supporting_txn_ids` |
| 3 | Every `txn_id` in `data/structuring_transfers.csv` appears exactly once |
| 4 | Every `classification` value is one of `structuring_component`/`unrelated` |
| 5 | `T1-001` through `T1-006` are all classified `structuring_component` (the core aggregation test) |
| 6 | `T1-007` and `T1-008` are both classified `unrelated` (the core over-implication test) |
| 7 | `structuring_report.md` exists, is non-empty, and cites at least 3 gold `txn_id`s |

These tests fail loudly and cannot be satisfied by a vague natural-language answer — the agent must produce a correctly-shaped CSV that gets the aggregation right and does not sweep the distractors in.

## 2. SK-judged qualitative scoring (`rubric.json`)

Run by `aml-agent judge --task task-008-structuring-below-threshold --workspace <ws>` after the agent exits.

The judge prompt loads `rubric.json`, `structuring_report.md` (the candidate's report), and `data/structuring_transfers.csv` (the ground-truth data, so the judge can verify citations). It scores seven dimensions on a 0-5 scale — see `rubric.json` for the full descriptions: `aggregation_identification`, `avoids_false_implication`, `typology_identification`, `evidence_grounding`, `avoids_unsupported_claims`, `evidence_traceability`, `explanation_quality`.

`overall_percentage` is the sum of scores divided by `35` (the max). `verdict` is `PASS` when `overall_percentage >= pass_threshold_overall` (default `0.7`). Each dimension also carries a `category` (`outcome_correctness`/`evidence_quality`/`process_quality`) that `judge_report.json`'s `rubric_by_category`/`outcome_correctness` fields aggregate separately from `overall_percentage` — see [docs/evidence-traceability-framework.md#outcome-correctness-vs-task-performance](../../docs/evidence-traceability-framework.md#outcome-correctness-vs-task-performance).

The judge writes `judge_report.json` to the workspace. [JudgeReportTests.cs](../../tests/AmlAgent.Tests/JudgeReportTests.cs) validates the file's shape and that the verdict is `PASS` when the report is present.
