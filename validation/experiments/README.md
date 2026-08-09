# Experiments

Live, API-cost experiments (as opposed to `validation/gold` and `validation/fixtures`,
which are deterministic and free to run repeatedly). Run via `aml-harness experiment ...`
(see `src/AmlAgent.Harness/ExperimentRepeatCommand.cs` and `ExperimentJudgeRepeatCommand.cs`).
Requires `OPENAI_API_KEY`.

## Items 6 + 7: repeated-run and judge-repeatability (built, demoed)

```bash
aml-harness experiment repeat --task task-006-temporal-network-anomaly-detection --runs 5 \
  --out validation/outputs/repeated_run_result.json

aml-harness experiment judge-repeat --workspace <a --keep-workspace'd run's temp dir> \
  --task task-006-temporal-network-anomaly-detection --runs 5 \
  --out validation/outputs/judge_repeatability_result.json
```

Demonstrated live on 2026-08-09 with `--runs 2` against task-006: two independent
agent+judge runs of the identical task/agent/model produced EGHR 0.3333 vs 0.5 and
rubric scores 0.7667 vs 0.8333; re-judging one FIXED report twice produced 0.8 vs
0.7667. Both are real, measured variance, not simulated -- see
`validation/outputs/demo_repeated_run_result.json` / `demo_judge_repeatability_result.json`
for the raw captured records (regenerable; not committed as permanent fixtures).

Both commands capture RAW per-run data only -- no consistency/reliability statistic
is computed or claimed, per the instructions ("do not yet invent a final scientific
consistency metric").

## Item 10: noise and distractor robustness (infra ready, variants not yet built)

`aml-harness experiment repeat` already captures `structured_findings` (parsed from
any `*findings*.csv` the task produces) on every run, which is exactly what's needed
to measure how an agent's network reconstruction degrades as distractor volume
increases. What's NOT yet built: the controlled task-007 variants themselves (no
distractors / few / many / irrelevant transactions / irrelevant KYC / irrelevant
relationships / incomplete evidence / contradictory evidence), each with the same
ground-truth suspicious network held constant per the instructions. Each variant is
a new `case-definition.json` + data files under a sibling task directory (e.g.
`tasks/task-007-multi-source-mule-network-no-distractors/`), following exactly the
pattern already established for `task-007-multi-source-mule-network` itself. Once
built, running `experiment repeat` against each variant and comparing
`structured_findings` across variants directly answers item 10 -- left as an
explicit next step rather than half-built here, since it is a genuine
task-authoring effort (like task-007 itself), not a tooling gap.

## Item 12: false-positive / innocent-entity protection (partially built)

The DETERMINISTIC half of this question -- can the scoring/classification layer
itself detect over- and under-reporting when it occurs -- is already fully answered
in `tests/AmlAgent.ResearchValidation/DiscriminationValidationTests.cs` (fixtures
07/08, task-007's `_over_reporting_innocent_entities` / `_under_reporting_suspicious_entities`).
The LIVE half -- does the AGENT actually over/under-report when genuinely run against
task-007 -- is directly measurable with the existing infrastructure:

```bash
aml-harness experiment repeat --task task-007-multi-source-mule-network --runs 5 \
  --out validation/outputs/false_positive_protection_result.json
```

Each run's `structured_findings` field contains the agent's real
`mule_network_findings.csv` classifications, ready to be checked against the known
innocent accounts (N150/N160/N170) and known suspicious accounts (N100/M201/M202/M301/EXT401)
using the same `ClassifyFindings` logic already in `DiscriminationValidationTests.cs`.
Not executed as part of this pass (deferred alongside item 10, since a meaningful
false-positive-rate reading needs more than 2-3 runs to be informative) -- the command
above is ready to run when a larger batch is wanted.
