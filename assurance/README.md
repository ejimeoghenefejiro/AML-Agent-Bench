# Assurance profile (prototype)

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
  [docs/dimension-mapping.md](../docs/dimension-mapping.md) for the same
  honesty accounting applied to the six PhD-proposal evaluation dimensions.
- **`PASS_WITH_CONDITIONS` is the realistic ceiling today**, not `PASS`,
  precisely because five of nine dimensions are unmeasured. That is by
  design in `AssuranceEngine.Decide` — see its doc comment.
