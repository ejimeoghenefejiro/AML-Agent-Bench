# task-008 — Structuring Below Reporting Threshold

Fills **level 2 (multi-record aggregation)** of the task complexity taxonomy
(`docs/research-problem.md#task-complexity-taxonomy`) — the gap between level
1 (`aml-transaction-network`, single claim to single evidence item) and
level 3 (`task-006`, temporal reasoning over a whole network). No standalone
task previously tested whether an agent can determine that a SET of
individually-compliant transactions is collectively suspicious.

Account `N200` makes six transfers to `M400` over nine days, each
individually under the institution's £10,000 reporting threshold (£8,700 –
£9,800), aggregating to **£55,200**. No single transaction would trigger a
report on its own — the pattern only emerges when the transactions are
considered together. Two distractor transactions exist to test
over-implication: one to a different destination account (`N250`), and one
to the same destination (`M400`) but three weeks earlier and far below the
threshold pattern, which must not be swept into the structuring finding just
because it shares a destination.

## What the agent must produce

| file | format | evaluator |
|---|---|---|
| `structuring_findings.csv` | CSV | xUnit (`Task008StructuringFindingsTests`) |
| `structuring_report.md` | Markdown | SK-as-judge (`rubric.json`) |

See [prompt.md](prompt.md) for the canonical task brief, [expected-behaviour.md](expected-behaviour.md) for what a good response looks like, and [tests.md](tests.md) for the full test plan.
