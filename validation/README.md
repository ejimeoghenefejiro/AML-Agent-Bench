# validation/

Research-validation data for AML-Agent-Bench, read by
`tests/AmlAgent.ResearchValidation` (a separate project from
`tests/AmlAgent.Tests` -- ordinary software correctness tests never live here,
and these experiments never live in `AmlAgent.Tests`).

This directory answers: *does the benchmark's measurement machinery (EGHR,
evidence traceability, canonical-case merging, evidence-integrity validation,
assurance decisions) actually measure what it claims to measure, correctly,
reproducibly, and robustly?* It is evidence for the PhD's methodology chapter,
not a feature surface.

- `fixtures/` -- controlled inputs: hand-authored agent-report variants (correct/
  incomplete/fabricated/hallucinated/etc.), corrupted canonical cases, the same
  logical case expressed in multiple storage formats.
- `gold/` -- manually-specified expected results for each fixture: expected
  atomic claims and EGHR, expected citation precision/recall/F1, human
  annotation examples. Never machine-generated from the code under test --
  gold values are authored independently so a test can actually fail.
- `experiments/` -- configuration for the repeated-run / judge-repeatability /
  noise-distractor / false-positive experiment runner (`aml-harness experiment
  ...`). These experiments call a real agent and/or a real LLM judge and cost
  real API time/money; they are not part of `dotnet test`.
- `outputs/` -- where a real experiment run's raw results land
  (`validation_result.json` and friends). Gitignored; regenerable, not source.

No thresholds are asserted here beyond what a given test can actually
demonstrate, and no scientific validity is claimed from a single run of any
experiment in `experiments/`.
