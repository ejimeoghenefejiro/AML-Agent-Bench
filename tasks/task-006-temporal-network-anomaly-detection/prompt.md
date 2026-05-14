# Task 006 — Temporal Network Anomaly Detection

You are working inside an AML investigation container.

The file `data/weekly_transfers.csv` contains a directed transfer ledger spanning three calendar weeks. Treat each row as one directed money movement between two accounts at a timestamp.

Your job is to detect **how the transaction network changes over time**, not just to score a static graph. Static risk scoring is out of scope for this task.

## Outputs

Produce **two** files at the sandbox root:

### 1. `temporal_anomaly_summary.csv` (machine-checked)

Exact columns, in this order:

```text
week,start_date,end_date,transfer_count,unique_accounts,total_value,new_accounts_count,high_risk_dest_count,sar_count,anomaly_score
```

Rules:

- One row per ISO calendar week, ordered chronologically. `week` values must be `week_1`, `week_2`, `week_3`.
- `start_date` and `end_date` are `YYYY-MM-DD`.
- `new_accounts_count` is the number of accounts that appear for the first time in this week (i.e. were not seen in any earlier week).
- `high_risk_dest_count` is the number of transfers whose `destination_country` is in `{AE, RU, CY, NG, TR}`.
- `anomaly_score` is between 0 and 1, rounded to 4 decimal places. Higher = more anomalous compared with the earliest week. Week 1 should have the lowest anomaly score.
- All numeric values rounded to 4 decimal places.

### 2. `temporal_anomaly_report.md` (LLM-judged)

A short compliance-style markdown report covering:

1. **Executive summary** — 2–3 sentences.
2. **Week-by-week analysis** — what changed, with **specific transaction IDs cited** as evidence. Cite at least one `txn_id` per claim.
3. **Anomaly detection** — which week is anomalous and *why*. Reference the metrics in your CSV.
4. **Risk indicators** — what specific patterns in the data (new intermediaries, high-risk destinations, SAR-linked flows, velocity) led to the conclusion.
5. **Facts vs. assumptions** — clearly separate what the data shows from what it might mean. Do not invent transactions, accounts, or amounts that are not present in the source file.

## Constraints

- Do not score individual transactions.
- Do not treat countries as graph nodes.
- Do not collapse the three weeks into a single static graph.
- Do not invent SAR cases not in the data.
- Use cautious, compliance-friendly language (e.g. "the data is consistent with…", not "this is money laundering").
- Do not emit either output file with extra columns or missing columns.
