# Assurance profile (prototype)

## Positioning relative to the PhD

**The PhD's doctoral core is measurement and validation of evidence
traceability** (see [docs/research-problem.md](../docs/research-problem.md)
and [docs/evidence-traceability-framework.md](../docs/evidence-traceability-framework.md)).
This assurance layer is a **downstream application**, not part of that core:
it shows one way traceability evidence (and the other metrics this benchmark
computes) might be consumed by a model-governance or human-review process.
It does not imply certification, regulatory approval, formal jurisdictional
compliance, that a benchmark score alone determines safe deployment, or that
unmeasured dimensions are passed — see [What this is not](#what-this-is-not-read-this-before-citing-it-anywhere)
below, which this repositioning does not weaken.

This folder is the first concrete step toward the long-term direction
sketched in `Proposal/AML Agent Bench Real World Assurance Profile.txt`:
evolving AML-Agent-Bench from a benchmark that prints PASS/FAIL into
something that produces a structured **AML Agent Assurance Profile** —
a machine-readable, evidence-backed answer to "is this agent suitable for
operational deployment, and under what conditions?"

That vision document's own Section 15 ("Recommended Next Engineering
Milestone") is explicit that the first priority should **not** be a
dashboard, policy engine, or jurisdiction-specific regulatory mapping — it
should be a stable `assurance_profile.json` schema. A follow-up document,
`Proposal/AML-Agent-Bench_CLI-Only_Assurance_Roadmap.txt`, picks up from
there once that schema existed and specifies a further set of CLI-only
maturity steps (status separation, runtime policy selection, required-vs-
optional thresholds, structured decision reasons, provenance) while
explicitly still deferring everything presentation-layer (dashboard,
jurisdiction profiles, PDF reports, continuous-assurance CI/CD, multi-agent
compare/regress subcommands) as "Later." This folder implements both
documents' near-term items, nothing beyond them.

## What's here

- **`policy.default.json`** and **`policies/bank-strict.json`** —
  *illustrative* example deployment policies (metric thresholds modelled on
  the vision document's own examples). Neither is a real bank's,
  regulator's, or jurisdiction's actual policy — see each file's own
  `description` field. A real deployment would replace these with an
  institution's own risk-appetite thresholds. Select one at runtime with
  `--policy <path>` (default: `policy.default.json`).
- Each threshold in a policy is marked `"required": true` (a critical gate —
  failing it blocks deployment outright) or `"required": false` (a warning —
  failing it downgrades the decision to `PASS_WITH_CONDITIONS` rather than
  blocking). `AmlAgent.Evidence.AssuranceEngine.ValidatePolicy` rejects a
  malformed policy (unknown direction, an impossible threshold for its unit)
  at load time with a clear error — it does not silently produce a decision
  from bad data, and it does not take down an otherwise-successful benchmark
  run either (see `Program.cs`'s handling around `AssuranceProfileBuilder.Build`).
- The harness (`src/AmlAgent.Harness`) evaluates the metrics it actually has
  against the selected policy's thresholds, writing `assurance_profile.json`
  alongside `bench_result.json` (workspace copy + archival copy in
  `assurance-profiles/`, mirroring how `results/` works). Every profile
  separates three concepts that must never be conflated: `execution_status`
  (did the agent process complete), `benchmark_verdict` (did xUnit + judge
  pass), and `assurance_decision` (does it meet the selected deployment
  policy) — a benchmark PASS is not a deployment PASS, and the roadmap
  document's own worked example (EGHR 50%, traceability recall 15.4%,
  benchmark still PASS) is exactly the failure mode this separation exists
  to prevent. Every non-PASS decision also carries structured `reasons`
  (metric, actual value, threshold, rule, severity) rather than only a
  prose string, and `provenance` records the git commit SHA, policy id and
  version, and SHA-256 hashes of the dataset and rubric used, so a decision
  can in principle be reproduced from what's recorded.

## Comparing and tracking runs (`compare` / `regress`)

`aml-harness compare <profile.json> <profile.json> ...` prints every run's
agent, model, task, policy, task performance, EGHR, evidence-traceability
precision/recall/F1, fabricated-citation count and assurance decision side
by side, and also writes `comparison_result.json` (compared runs,
comparable vs. excluded dimensions, warnings) for automation. It never
fabricates a value for a `not_implemented` dimension — only the four
metrics this benchmark actually measures ever appear.

`aml-harness regress --baseline <p.json> --candidate <p.json>` diffs two
profiles metric-by-metric with direction-aware better/worse labelling,
lists any threshold that newly **failed** or newly **passed** between the
two runs, and reports whether the deployment decision itself got worse
(`ASSURANCE REGRESSION DETECTED`). Also writes `regression_result.json`.
Exit code 1 on a detected regression, so it's usable as an automation gate
without any CI/CD system existing yet.

Both commands run `AmlAgent.Evidence.CompatibilityCheck` first and print a
`WARNING` for any pair of runs that differ in task, policy id/version,
benchmark version, dataset, or which dimensions are required — the
comparison still runs, but is clearly labelled non-equivalent rather than
silently presented as apples-to-apples.

## Exit codes

`aml-harness` (run mode): `0` completed + benchmark passed + assurance PASS
(or no assurance profile applicable); `1` execution failure (agent process
failed); `2` benchmark failure (xUnit/judge failed); `3` benchmark passed,
`PASS_WITH_CONDITIONS`; `4` benchmark passed, `NOT_READY_FOR_DEPLOYMENT`;
`5` invalid policy/configuration. `compare`/`regress`: `0` ok, `1`
regression detected (`regress` only), `6` invalid comparison. Full table
also printed by `--help`.

## Schema validation and provenance

Every generated profile is checked against
`AmlAgent.Evidence.AssuranceProfileSchema` before it's written — required
top-level fields, metric/decision/policy/provenance structure, valid
status/decision enum values, and a well-formed `result_hash`. A profile
that fails validation is rejected with every violation listed (not just the
first), and — like a malformed policy — this does not take down an
otherwise-successful benchmark run; it surfaces as exit code `5`.

`provenance` records: run id, timestamps, execution mode, benchmark
version, git commit SHA, policy id/version/file hash, dataset hash, rubric
hash, a task fingerprint (hash of the task's prompt/rubric/evidence-
annotations, standing in for a task version number that doesn't exist yet),
a benchmark-config hash (task+model+steps+mode+policy in one fingerprint),
model identifier, temperature, judge model/config, and the .NET
runtime/OS. `agent_version` is honestly recorded as `"unversioned"` (no
version scheme exists for in-repo agents yet) rather than omitted, and
`random_seed` is recorded as `null` with an explicit note: OpenAI's `seed`
parameter, where available, is documented by the provider as best-effort,
not a determinism guarantee, so it is not wired up or claimed here. The
`reproducibility_note` field states plainly what *is* deterministic
(evidence scoring, traceability, policy evaluation, all hashes — unit
tested) versus what isn't (the underlying LLM's own output).

## What this is not (read this before citing it anywhere)

- **Not a certification.** Section 14 of the vision document is explicit
  that during the PhD this should be described as an *assurance profile*,
  not a formal certification — a certification regime would require
  recognised standards, independent governance, institutional adoption and
  regulatory recognition that a PhD prototype cannot provide.
- **Not jurisdiction-aware.** The core benchmark and this policy stay
  jurisdiction-neutral, per Section 6 of the vision document. There is no
  UK/US/Nigeria-specific regulatory mapping implemented — that is future
  work, tracked as a gap, not silently assumed.
- **Only measures what the benchmark actually measures.** Of the nine
  trust/assurance dimensions the vision document lists, this prototype
  evaluates four against policy thresholds: EGHR, evidence-traceability F1,
  fabricated-citation count, and task performance (rubric score, standing
  in for detection performance since there is no labelled multi-case
  detection dataset yet). The other five — fairness disparity, explanation
  faithfulness, audit completeness, expected calibration error, and
  run-to-run consistency — are listed in every generated profile under
  `not_evaluated_dimensions` with status `not_implemented`. **A generated
  profile never shows a fabricated PASS for a dimension that was not
  actually measured.** See
  [docs/research-scope-mapping.md](../docs/research-scope-mapping.md) for the
  same honesty accounting applied to the Evidence Traceability Profile's
  components (EGHR is retained here as a legacy/secondary metric, not the
  PhD's primary construct — see
  [docs/evidence-traceability-framework.md](../docs/evidence-traceability-framework.md#legacy-eghr-metric)).
- **`PASS_WITH_CONDITIONS` is the realistic ceiling today**, not `PASS`,
  precisely because five of nine dimensions are unmeasured. That is by
  design in `AssuranceEngine.Decide` — see its doc comment.
