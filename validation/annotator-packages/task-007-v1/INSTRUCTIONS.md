# Task 007 Annotation Instructions — v1 (frozen 2026-08-29)

> **This version is frozen.** Once distributed to an annotator, this file must
> not change — a correction or improvement becomes v2, in its own
> `validation/annotator-packages/task-007-v2/` directory, never an edit made
> in place here. This is what "versioned package" means in practice: an
> annotator's work is always against a specific, fixed, citable instruction
> set.

## What you are doing

You are independently determining the **material claims** a strong
investigative report on this case should make, and — for each one — what
case evidence is **required** (or **acceptable as an alternative**) to
support it. You are not reviewing anyone else's report. You are not told
what another annotator concluded, and you will not see it before you submit
your own independent annotation.

This directly matches the six-decision annotation framework in
`docs/evidence-annotation-protocol.md` (Materiality, Relevance, Support,
Sufficiency, Necessity, Ambiguity) — read that document first if you have not
already; it explains the concepts below in full, with worked examples.

## What is (and is not) in this package

- `prompt.md` — the exact brief a candidate AI agent receives for this task.
  Read it first: it tells you what investigative question the case is
  answering and what output shape a report takes.
- `case_data/` — every raw source file the case is built from: two
  transaction feeds (CSV and JSON), an archived transaction ledger (Parquet),
  a relationship/watchlist graph (GraphML), informal investigator notes, and
  the case-definition manifest describing how the sources combine. This is
  the same underlying data an agent works from.
- `template.json` — the file you fill in and submit (see below).

**What is deliberately NOT in this package**, so your judgement is
independent: this task's `evidence-annotations.json` (the current
single-author gold answers this annotation round exists to check against),
`expected-behaviour.md` (the answer key describing the correct network
reconstruction and which accounts should be cleared), and `rubric.json` (the
scoring criteria, which would bias you toward matching a rubric rather than
reasoning from the evidence). If you encounter any of these files by any
other means before submitting your annotation, note it in your submission —
your annotation may need to be discarded and redone.

## What to produce

For each material claim you identify — a claim materially important to the
investigative conclusion, per the Materiality question in
`docs/evidence-annotation-protocol.md` — write one entry with:

- `claim_id`: your own short identifier (e.g. `MC1`, `MC2`, ...). It does not
  need to match any other annotator's numbering or any existing scheme.
- `text`: the claim itself, in your own words.
- `required`: the evidence ids (from the case data — transaction ids,
  relationship ids, watchlist entries, etc.) that MUST be cited for the claim
  to count as adequately supported.
- `acceptable_alternatives` (optional): other complete evidence sets that
  would be equally valid INSTEAD of `required` — use this when you believe
  more than one citation path independently establishes the same fact (the
  Ambiguity question).
- `corroborating` (optional): evidence that strengthens the claim but is not
  itself required.
- `rationale`: a sentence explaining your reasoning — this is what makes
  disagreement between annotators resolvable later, rather than just visible.

Fill in `template.json` with your `annotator_id` and your claims, following
the schema `AmlAgent.Evidence.GoldClaimAnnotationReader` parses (see
`validation/annotator-packages/task-007-v1/template.json` for the exact
shape, and `docs/evidence-annotation-protocol.md` for the underlying
concepts).

## What happens after you submit

Your file is saved, unmodified, as your own permanent record (see
`validation/annotations/README.md`) — it is never edited or overwritten,
even during adjudication. Once at least one other annotator has also
submitted independently, `AmlAgent.Evidence.ClaimAnnotationAdjudication`
compares the two submissions claim-by-claim and produces a worksheet of
exactly where you agreed and disagreed. A human adjudicator (which may or
may not be you) then resolves each disagreement explicitly — never by
automatic majority vote or by picking one annotator's answer as default —
and the resolutions become a separate, clearly-marked "adjudicated" file.
`AmlAgent.Evidence.AgreementStatistics` then computes raw and chance-corrected
agreement (Cohen's kappa for two annotators, Fleiss' kappa for three or
more) over your and the other annotator(s)' pre-adjudication submissions.
