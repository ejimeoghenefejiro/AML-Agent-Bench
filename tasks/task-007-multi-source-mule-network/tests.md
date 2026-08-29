# Task 007 — Test Plan

Outputs are evaluated by **two complementary layers**, same model as task-006.

## 1. Structural xUnit tests (deterministic)

Run by `dotnet test` against the workspace once the agent has exited.
Implemented in [Task007MuleNetworkFindingsTests.cs](../../tests/AmlAgent.Tests/Task007MuleNetworkFindingsTests.cs).

| # | Assertion |
|---|-----------|
| 1 | `mule_network_findings.csv` exists at workspace root |
| 2 | Schema is exactly `account_id,classification,confidence,supporting_txn_ids` |
| 3 | Every `classification` value is one of `victim`/`mule`/`exit_point`/`watchlist_match`/`cleared` |
| 4 | `confidence` is in `[0, 1]` for every row |
| 5 | `N100` is classified `victim` |
| 6 | `M201`, `M202`, `M301` are all classified `mule` |
| 7 | `EXT401` is classified `exit_point` |
| 8 | `N150` and `N160` are **not** classified `mule`, `exit_point`, or `watchlist_match` (the core innocent-account test) |
| 9 | `mule_network_report.md` exists, is non-empty, and cites at least 3 gold `txn_id`s |
| 10 | `case_manifest.json` was actually generated in the workspace (proves the multi-source case pipeline ran, not just that the task has files) |

These tests fail loudly and cannot be satisfied by a vague natural-language answer — the agent must produce a correctly-shaped CSV that gets the core network right and does not implicate the innocent accounts.

## 2. SK-judged qualitative scoring (`rubric.json`)

Run by `aml-agent judge --task task-007-multi-source-mule-network --workspace <ws>` after the agent exits.

The judge prompt loads `rubric.json`, `mule_network_report.md` (the candidate's report), and `data/transactions.csv` (the canonical, deduplicated ground-truth data, so the judge can verify citations). It scores eight dimensions on a 0–5 scale — see `rubric.json` for the full descriptions: `network_identification`, `evidence_grounding`, `avoids_unsupported_claims`, `evidence_traceability`, `avoids_false_implication`, `typology_identification`, `explanation_quality`, `audit_trail_awareness`.

`overall_percentage` is the sum of scores divided by `40` (the max). `verdict` is `PASS` when `overall_percentage >= pass_threshold_overall` (default `0.7`). Each dimension also carries a `category` (`outcome_correctness`/`evidence_quality`/`process_quality`) that `judge_report.json`'s `rubric_by_category`/`outcome_correctness` fields aggregate separately from `overall_percentage` — see [docs/evidence-traceability-framework.md#outcome-correctness-vs-task-performance](../../docs/evidence-traceability-framework.md#outcome-correctness-vs-task-performance).

The judge writes `judge_report.json` to the workspace. [JudgeReportTests.cs](../../tests/AmlAgent.Tests/JudgeReportTests.cs) validates the file's shape and that the verdict is `PASS` when the report is present.

`evidence-annotations.json` also defines six `material_claims` (fix #7) — task-authored materiality and Required/AcceptableAlternatives reference evidence for the network/typology conclusions in `expected_conclusions`. The judge prompt asks the LLM only to identify which evidence ids the candidate's report cites for each claim; `ClaimLevelScoring` scores adequacy deterministically. This populates `judge_report.json`'s `material_claims` array and `assurance_profile.json`'s `claim_support_coverage` — see [docs/evidence-traceability-framework.md#claim-support-coverage-csc](../../docs/evidence-traceability-framework.md#claim-support-coverage-csc).

## 3. Case-loading / evidence-integrity layer (new for this task)

Unlike task-001 and task-006, this task's `environment/` includes a `case-definition.json`. The harness's `StageCanonicalCaseIfPresent` step (see `src/AmlAgent.Harness/Program.cs`) runs automatically before the agent starts: it resolves all four adapters (csv/json/parquet/graphml), merges them via `CanonicalCaseMerger`, validates cross-source evidence references via `EvidenceIntegrityValidator`, and writes `case_manifest.json` plus the canonical `data/transactions.csv` / `data/relationships.json` exports the agent actually reads. This is exercised independently and exhaustively by `tests/AmlAgent.Tests/Adapters/CaseLoaderTests.cs` and `EvidenceIntegrityValidatorTests.cs` against this exact scenario shape, not just implicitly through a live agent run.
