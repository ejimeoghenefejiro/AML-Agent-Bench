# Task 008 — Structuring Below Reporting Threshold

You are working inside an AML investigation container.

The file `data/structuring_transfers.csv` contains every outbound transfer
from account `N200` over a six-week window. Each row is one directed money
movement: `txn_id,timestamp,source_account,destination_account,amount,source_country,destination_country,sar_linked`.

This institution's reporting threshold for a single transaction is **£10,000**.
No single transaction in this file exceeds that threshold. Your job is to
determine whether the transactions, taken **together**, still constitute a
suspicious pattern — this is a test of multi-record aggregation, not
single-transaction scoring. Do not conclude "nothing to report" just because
every individual row is compliant on its own.

## Outputs

Produce **two** files at the sandbox root:

### 1. `structuring_findings.csv` (machine-checked)

Exact columns, in this order:

```text
txn_id,classification,amount,supporting_txn_ids
```

Rules:

- One row per transaction in `data/structuring_transfers.csv` — every `txn_id`
  in the source file must appear exactly once.
- `classification` must be exactly one of: `structuring_component`, `unrelated`.
  - `structuring_component`: this transaction is part of a coordinated
    below-threshold pattern (same destination, tight time window, amounts
    consistently near-but-under the reporting threshold, aggregating to a
    materially large total).
  - `unrelated`: this transaction does not belong to that pattern, even if it
    shares some superficial similarity (e.g. the same destination account, or
    an amount also under the threshold) with transactions that do.
- `amount` is the transaction's amount, copied from the source file.
- `supporting_txn_ids` is a semicolon-separated list of the OTHER `txn_id`s
  that, together with this one, make up the aggregation pattern you're citing
  it as part of (empty for `unrelated` rows).

### 2. `structuring_report.md` (LLM-judged)

A compliance-style markdown report covering:

1. **Executive summary** — 2–3 sentences: what pattern was found, and its
   aggregate value.
2. **Aggregation evidence** — list every transaction you classify as part of
   the pattern, cite each `txn_id`, and state the aggregate total. Explain
   *why* the amounts and timing are consistent with deliberate structuring
   rather than coincidence (e.g. how close each amount sits to the £10,000
   threshold, how tightly clustered in time the transactions are).
3. **Accounts/transactions reviewed and cleared** — explicitly name every
   transaction you considered but ruled out, and state a specific reason —
   sharing a destination account or also being under the threshold is not by
   itself a reason to include a transaction in the pattern.
4. **Typology** — name the typology (structuring / smurfing) and use
   cautious, regulator-appropriate language; do not assert a criminal
   conclusion.
5. **Facts vs. assumptions** — clearly separate what the transaction data
   shows from what it might mean.

## Constraints

- Every transaction ID, account ID, or amount you cite must exist in
  `data/structuring_transfers.csv`.
- Do not classify a transaction as `structuring_component` solely because it
  shares a destination account with other structuring transactions, or
  solely because it is also under the reporting threshold — the pattern
  requires the combination of destination, tight timing, and near-threshold
  amount together.
- Do not omit the "reviewed and cleared" section.
- Do not emit `structuring_findings.csv` with extra columns, missing columns,
  a different column order, or a missing/duplicated `txn_id`.
