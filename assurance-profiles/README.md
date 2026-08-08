# assurance-profiles/

Every run of `aml-harness` that produces a judge report also writes a
consolidated `assurance_profile.json` here, alongside the copy left in the
temp workspace — mirroring how `results/bench_result.json` archives work.

See [assurance/README.md](../assurance/README.md) for what this file is,
what policy it's evaluated against, and — importantly — what it does *not*
yet measure (five of nine assurance dimensions are honestly marked
`not_implemented` rather than faked).

## File naming

```text
assurance-profiles/<UTC-timestamp>-<task>-<agent>.json
```

## What's in each file

- `disclaimer` — states plainly that this is a PhD research prototype, not
  a certification.
- `status_summary` — three deliberately separate fields: `execution_status`
  (did the agent process complete), `benchmark_verdict` (xUnit + judge
  PASS/FAIL), and `assurance_decision` (this policy's decision). A
  benchmark PASS is never the same thing as a deployment PASS — these can
  and do disagree.
- `agent` / `benchmark` / `scenario_pack` / `jurisdiction_profile` — identity
  fields.
- `operational_capabilities` — which capabilities this task actually
  exercises (e.g. `anomaly_detection`, `evidence_based_case_reasoning`),
  read from `tasks/<id>/capabilities.json`. An untagged task shows an empty
  array rather than a guessed value.
- `policy` — id, name, version and path of the policy this run was
  evaluated against (selectable at runtime with `--policy <path>`).
- `metrics` — each policy-defined metric: measured value, threshold,
  whether it's `required` (critical gate) or optional (warning), and
  PASS / FAIL / NOT_EVALUATED.
- `not_evaluated_dimensions` — assurance dimensions the benchmark doesn't
  yet measure at all (fairness, faithfulness, audit completeness, ECE,
  consistency).
- `deployment_decision` — `PASS`, `PASS_WITH_CONDITIONS`, or
  `NOT_READY_FOR_DEPLOYMENT`, a prose reason, and a structured `reasons`
  array (metric, actual value, threshold, rule, severity) for every failed
  or unevaluated metric.
- `deployment_restrictions` — the illustrative permitted / human-approval /
  not-permitted use lists from the selected policy.
- `evidence_summary` — the claims and citations behind the EGHR and
  traceability numbers, pulled from the same judge output.
- `provenance` — run ID, workspace path, timestamps, execution mode,
  benchmark version, git commit SHA, policy id/version/file hash, dataset
  hash, rubric hash, a task fingerprint (stands in for a task version
  number, which doesn't exist yet), a benchmark-config hash, model
  identifier, temperature, judge model/config, .NET runtime/OS, and a
  `reproducibility_note` stating plainly what is and isn't deterministic
  (the pipeline's own scoring is; the underlying LLM's output isn't,
  since no provider-guaranteed seed is configured) — enough to state what
  exact benchmark version, task version, model, policy and dataset a
  decision came from. See [assurance/README.md](../assurance/README.md#schema-validation-and-provenance)
  for the full field-by-field rationale.
- `result_hash` — SHA-256 of the profile's own content (excluding this
  field), for basic tamper-evidence.

This directory is gitignored (see `.gitignore`) for the same reason
`results/` is — it accumulates one file per run. Add specific profiles with
`git add -f assurance-profiles/<file>.json` when they're worth citing.
