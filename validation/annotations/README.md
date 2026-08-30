# Annotations

This directory is for REAL, independently-submitted `GoldClaimAnnotationSet`
files (`AmlAgent.Evidence.GoldClaimAnnotationReader`), once a real annotation
round happens (v0.3 validation-priorities item 1). **No real annotations
have been collected yet** — this directory intentionally has no data files
in it today, only this README describing the convention real submissions
will follow.

## Directory convention

```
validation/annotations/<task-id>-v<package-version>/
  <annotator_id>.json       -- one annotator's raw, independent submission
  <annotator_id>.json       -- another annotator's raw, independent submission
  adjudicated.json          -- the final, adjudicated GoldClaimAnnotationSet
  agreement_report.json     -- raw + chance-corrected agreement between the
                                pre-adjudication submissions (see below)
```

Example, once task-007's v1 round has real submissions:

```
validation/annotations/task-007-v1/
  H01.json
  H02.json
  adjudicated.json
  agreement_report.json
```

## Rules this convention exists to enforce

- **Pre-adjudication annotations are never edited or overwritten.**
  `<annotator_id>.json` is that annotator's permanent record of what they
  independently concluded, before seeing anyone else's answer or any
  adjudication discussion. If an annotator wants to revise their view after
  seeing disagreements, that revision happens in the adjudication discussion
  and shows up in `adjudicated.json` and its own rationale, not by editing
  their original file.
- **`adjudicated.json` is a separate, clearly-marked file**, produced by
  `AmlAgent.Evidence.ClaimAnnotationAdjudication.Adjudicate` from an
  adjudicator's explicit, recorded resolutions — never generated
  automatically (no majority vote, no "pick the first annotator", no
  heuristic). Its `adjudication_status` field reads `"adjudicated"`, so
  nothing downstream can mistake it for a single annotator's raw opinion.
- **`agreement_report.json`** records `AmlAgent.Evidence.ClaimAnnotationAdjudication.Compare`'s
  raw claim-by-claim comparison plus `AmlAgent.Evidence.AgreementStatistics`'
  chance-corrected kappa, computed over the RAW pre-adjudication submissions
  (agreement is measured on independent judgement, not on the adjudicated
  result, which by definition has no disagreement left to measure).

## What this is not

This is not multi-annotator VALIDATION yet — it is the infrastructure and
convention a real round needs. See `docs/evidence-annotation-protocol.md
#multi-annotator-validation` for the current status (not yet performed) and
what "validated" does and does not mean once real data exists, per
`docs/validation-plan.md#what-validated-does-not-mean-here`.
