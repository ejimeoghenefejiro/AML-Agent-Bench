# task-006 — Temporal Network Anomaly Detection

Detect how an AML transaction network **changes over time**, not just whether
it is risky in aggregate. The dataset spans three calendar weeks:

- **Week 1** — normal, low-volume flow among five regular accounts (`N001`–`N005`).
- **Week 2** — sudden spike in volume routed through three previously-unseen
  intermediary accounts (`X001`, `X002`, `X003`).
- **Week 3** — funds exit `X003` to five external accounts in high-risk
  jurisdictions (`AE`, `RU`, `CY`).

## What the agent must produce

| file                              | format    | evaluator                    |
|-----------------------------------|-----------|------------------------------|
| `temporal_anomaly_summary.csv`    | CSV       | xUnit (`Task006SummaryTests`)|
| `temporal_anomaly_report.md`      | Markdown  | SK-as-judge (`rubric.json`)  |

See [prompt.md](prompt.md) for the canonical task brief, [expected-behaviour.md](expected-behaviour.md) for what a good response looks like, and [tests.md](tests.md) for the full test plan.

## Why this task matters for the PhD

The first task (`aml-transaction-network`) tested static graph reasoning and a
single risk score. This task adds:

- **time-window reasoning** — bucketing by calendar week
- **graph evolution** — new accounts emerging between weeks
- **anomaly detection** — spotting the spike *as a change*, not a snapshot
- **evidence-based writing** — citing transaction IDs, separating fact from inference
- **compliance tone** — language that a regulator would accept
- **reproducibility** — small synthetic dataset, deterministic structural tests
- **regtech-aligned grading** — a structured rubric that an LLM judge applies

Together these are closer to what an actual financial-crime analyst is expected
to do than generic coding benchmarks measure.
