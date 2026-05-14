# Task 006 — Expected Agent Behaviour

A well-performing agent on this task should:

## On the data

- Parse `data/weekly_transfers.csv` correctly, including the `timestamp` field as a date-time and the `sar_linked` field as a 0/1 integer.
- Bucket transfers into three calendar weeks based on `timestamp`:
  - week_1: 2026-01-05 → 2026-01-11
  - week_2: 2026-01-12 → 2026-01-18
  - week_3: 2026-01-19 → 2026-01-25
- Track which accounts appear for the first time in each week to compute `new_accounts_count`.
- Count `high_risk_dest_count` against the destination country set `{AE, RU, CY, NG, TR}`.

## On the network

- Treat accounts as nodes and transfers as directed edges.
- Reason about *how the network evolves between weeks*, not just static structure.
- Notice new intermediary accounts appearing in a later week and chaining between previously-disconnected actors.
- Notice when funds exit to high-risk jurisdictions.

## On the report

- Write in a compliance-investigator tone, hedged and evidence-based.
- Cite transaction IDs (`T1-001`, `T2-014`, ...) when making claims, not just account names.
- Distinguish between observed facts ("`X003` received funds from three different sources in week 2") and assumptions/interpretations ("this is consistent with a layering pattern").
- Never assert that any party is guilty of money laundering — describe the data, not the legal conclusion.

## On the score

- `anomaly_score` should be strictly increasing across the three weeks, with `week_1 < week_2 < week_3`.
- `week_3` should have an `anomaly_score` close to 1.0; `week_1` close to 0.0.

## Failure modes to avoid

- Producing only one of the two output files.
- Inventing transaction IDs or accounts that are not in the source data.
- Confusing rows with accounts ("there were 32 accounts" — wrong; there are far fewer accounts than transfers).
- Treating the three weeks as a single graph and losing the temporal signal.
- Overclaiming ("this proves money laundering occurred").
- Producing the CSV with extra columns, missing columns, or wrong column order.
