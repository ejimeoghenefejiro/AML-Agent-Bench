# Task 008 — Expected Agent Behaviour

A well-performing agent on this task should:

## On aggregation (the core test of this task)

- Recognise that T1-001, T1-002, T1-003, T1-004, T1-005, and T1-006 (£9,200 /
  £8,700 / £9,500 / £8,900 / £9,100 / £9,800) are all under the £10,000
  reporting threshold individually, but aggregate to **£55,200** to the same
  destination (`M400`) within a nine-day window (2026-03-02 to 2026-03-10) —
  a textbook structuring/smurfing signature.
- Not conclude "no suspicious activity" on the basis that every individual
  transaction is compliant — that is precisely the failure mode this task
  exists to catch.
- Cite the specific `txn_id`s (T1-001 through T1-006) as the aggregation
  evidence, not a vague reference to "several transfers".

## On the distractors (the other core test)

- Explicitly clear `T1-007` (£7,300 to `N250`, a different destination
  entirely — no shared destination with the structuring cluster at all).
- Explicitly clear `T1-008` (£3,000 to `M400`, the *same* destination as the
  structuring cluster, three weeks *before* it starts, and far below the
  £8,700–£9,800 near-threshold band the real pattern sits in). The agent
  must reason about the COMBINATION of destination, timing, and amount — an
  agent that flags T1-008 purely because it shares a destination with
  `structuring_component` transactions has failed the core test of this
  task, symmetrically to how an agent that misses the real pattern has.
- Give a specific reason for clearing each distractor, not a blanket
  statement.

## On the report

- Name the typology as structuring or smurfing.
- State the aggregate total (£55,200) explicitly.
- Use cautious, compliance-appropriate language; do not assert a criminal
  conclusion.
- Clearly separate observed facts (amounts, timing, destination) from
  interpretation (that this is consistent with deliberate structuring).

## Failure modes to avoid

- Reporting "no findings" because no single transaction exceeds the
  threshold.
- Classifying `T1-007` or `T1-008` as `structuring_component`.
- Omitting the "reviewed and cleared" section.
- Citing a `txn_id` that does not exist in `data/structuring_transfers.csv`.
- Producing `structuring_findings.csv` with a missing, duplicated, extra, or
  reordered column, or missing a row for any transaction in the source file.
