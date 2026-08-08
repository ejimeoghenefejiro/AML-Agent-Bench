# Task 007 — Expected Agent Behaviour

A well-performing agent on this task should:

## On the case and its provenance

- Read `case_manifest.json` before writing the report, and note its `evidence_integrity.status` and any entries under `merge_conflicts`.
- Recognise the `T2-001` timestamp merge conflict (correspondent feed says 2026-02-02, archive says 2026-02-05) and mention the discrepancy rather than treating either value as unambiguous fact.
- Use `data/transactions.csv` and `data/relationships.json` (the canonical, deduplicated case) as primary evidence, not the raw per-source files under `data/` — and not double-count a transaction that happens to appear in more than one raw source.
- Treat `data/case-notes.md` as unverified context, not evidence to cite directly.

## On the network

- Trace the fraud path: `N100` (victim) → `M201`/`M202` (first-layer mules, receiving `T1-001`/`T1-002`) → `M301` (aggregator, receiving `T1-003`/`T1-004`) → `EXT401` (offshore exit, `T2-001`).
- Note the independent watchlist corroboration on `M301` (a prior SAR from another institution, via the `flagged_by_watchlist` relationship — this relationship has no transaction evidence attached, which is expected and not an error).
- Correctly identify the fraud typology as an authorised-push-payment / social-engineering scam into a mule network with an offshore exit.

## On the innocent accounts (the core test of this task)

- Explicitly clear `N150`: it has a real relationship-graph edge to `M201` (`T1-005`), but the transaction predates any sign M201 was compromised and is a small, recurring, unrelated payment — the agent must reason about *timing and pattern*, not just "this account touched a mule account."
- Explicitly clear `N160` and `N170`: their transaction `T2-002` has **no relationship-graph edge at all** connecting it to the mule network — it only co-occurs in the same correspondent-bank export batch. An agent that includes them because they "appear in the case data" has failed the core test of this task.
- A genuinely strong report names all three innocent accounts and gives a specific reason for clearing each one, not a blanket "no other suspicious activity found."

## On the report

- Cite specific `txn_id`s from `data/transactions.csv` for every factual claim about money movement.
- Use cautious, compliance-appropriate language; do not assert a criminal conclusion.
- Clearly separate observed facts (what the transactions and relationships show) from interpretation (what pattern they are consistent with).

## Failure modes to avoid

- Classifying `N150`, `N160`, or `N170` as `mule`, `exit_point`, or `watchlist_match`.
- Omitting the "accounts reviewed and cleared" section, or clearing accounts without a stated reason.
- Citing a `txn_id` that does not exist in `data/transactions.csv`.
- Ignoring `case_manifest.json` and its merge-conflict/evidence-integrity information entirely.
- Double-counting a transaction because it appears in more than one raw per-source file (e.g. treating `T1-003` from both the CSV and the JSON export as two separate transactions).
- Producing `mule_network_findings.csv` with extra columns, missing columns, or the wrong column order.
