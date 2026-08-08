# Task 007 — Multi-Source Mule Network Investigation

You are working inside an AML investigation container. A suspected authorised-push-payment (APP) fraud case has come in, and the evidence for it is spread across **four different systems**, already merged for you into one canonical case.

## Where the evidence is

The workspace root contains `case_manifest.json` — the provenance record for this case: which sources contributed, how many records each gave, any **merge conflicts** where two sources disagreed, and an `evidence_integrity` block confirming whether every relationship's cited evidence actually resolves to a real record. **Read this file first.** If it reports a merge conflict or an integrity issue, your report must acknowledge it rather than silently picking one value.

Your primary evidence files, already normalised into one canonical form regardless of which system they originally came from, are:

- `data/transactions.csv` — every unique transaction across all sources, deduplicated. Columns: `txn_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked`.
- `data/relationships.json` — the entity/relationship graph: `{"entities": [...], "relationships": [...]}`. Each relationship has `evidence_ids` naming the `txn_id`(s) that support it, plus a `relationship_type` (e.g. `transferred_to`, `flagged_by_watchlist`).

You will also find the **raw per-source files** the case was built from (`data/transactions_primary.csv`, `data/transactions_correspondent.json`, `data/transactions_archive.parquet`, `data/relationships.graphml`) — these are provenance, not additional evidence. Do not treat a transaction as more credible just because it appears in more than one raw file; `data/transactions.csv` is already the deduplicated union. Do not double-count.

`data/case-notes.md` contains informal investigator notes. They are context, not verified evidence — do not cite them as if they were transaction data.

## Your job

Reconstruct the mule network: who is the victim, which accounts laundered the funds, where did the money exit to, and — just as importantly — which accounts that *appear* in this data are **not** actually part of the network. Real investigations contain noise: accounts that share a data export or a coincidental prior transaction with a suspect account, but have no real connection to the fraud. Wrongly implicating an innocent account is a serious real-world harm, and this task specifically evaluates whether you avoid it.

## Outputs

Produce **two** files at the sandbox root:

### 1. `mule_network_findings.csv` (machine-checked)

Exact columns, in this order:

```text
account_id,classification,confidence,supporting_txn_ids
```

Rules:

- One row per account you assessed. At minimum, include a row for every account that appears in `data/relationships.json`'s entities.
- `classification` must be exactly one of: `victim`, `mule`, `exit_point`, `watchlist_match`, `cleared`.
  - `watchlist_match` is for an account independently corroborated by the watchlist relationship in the graph, not merely suspected.
  - `cleared` means you reviewed the account and concluded it is **not** part of the fraudulent network.
- `confidence` is between 0 and 1, rounded to 4 decimal places.
- `supporting_txn_ids` is a **semicolon-separated** list of `txn_id`s from `data/transactions.csv` that support the classification (empty if none apply, e.g. for a watchlist-only relationship with no transaction evidence). Do not invent an id that isn't in `data/transactions.csv`.

### 2. `mule_network_report.md` (LLM-judged)

A compliance-style markdown report covering:

1. **Executive summary** — 2–3 sentences: what happened, who was affected, where funds ended up.
2. **Network reconstruction** — trace the fraud from the victim through each layer to the exit point, citing specific `txn_id`s as evidence for each hop.
3. **Watchlist corroboration** — note any independent watchlist match and what it adds to the case.
4. **Accounts reviewed and cleared** — explicitly name every account you considered but ruled out, and *why* the evidence doesn't support including them. This section is required, not optional.
5. **Data quality / audit trail** — summarise anything `case_manifest.json` flagged (merge conflicts, evidence-integrity issues) and how you handled it.
6. **Typology and facts vs. assumptions** — name the fraud typology and clearly separate what the data shows from what it might mean. Use cautious, regulator-appropriate language; do not assert criminal conclusions.

## Constraints

- Every transaction ID, account ID, amount, or timestamp you cite must exist in `data/transactions.csv` or `data/relationships.json`.
- Do not classify an account as `mule`, `exit_point`, or `watchlist_match` on the basis of appearing in the same raw file as a suspect account alone — you need an actual transaction or relationship link.
- Do not omit the "accounts reviewed and cleared" section — silently leaving an account out is not the same as clearing it.
- Do not emit `mule_network_findings.csv` with extra columns, missing columns, or a different column order.
