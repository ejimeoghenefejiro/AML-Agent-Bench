# Evidence Annotation Protocol

> How reference (gold) evidence for AML-Agent-Bench tasks is, and should be,
> annotated. See [docs/evidence-traceability-framework.md](evidence-traceability-framework.md)
> for the claim–evidence model this protocol produces data for.

## Current status (read this first)

The gold evidence set currently used by `tasks/task-006-temporal-network-anomaly-detection` and `tasks/task-007-multi-source-mule-network` (`evidence-annotations.json` in each task's directory) is **hand-curated by a single author** on a small, synthetic dataset. It is not yet independently annotated, not yet multi-rater, and not yet validated for inter-rater agreement. This is stated plainly rather than implied otherwise — see [docs/research-scope-mapping.md](research-scope-mapping.md) for the honest status of every component this protocol describes.

## Unit of annotation

Annotation happens at five levels, not just "is this citation correct":

- **material investigative claim** — a discrete, checkable assertion in an investigative output (e.g. "N100 sent funds to M201") that the benchmark treats as requiring evidence.
- **evidence item** — a single identifiable record (transaction, account, relationship, SAR, watchlist entry) in the case.
- **claim–evidence link** — a specific `(claim, evidence)` pair the annotator judges supports the claim.
- **evidence necessity** — whether a given evidence item is *required* for the claim to be considered supported, or merely *helpful*.
- **evidence sufficiency** — whether the full set of evidence linked to a claim is *adequate* to support the claim's strength and scope, not just individually relevant.

## Annotation decisions

Formally, an annotator makes six named decisions per candidate claim:

| Decision | Annotation question |
|---|---|
| Materiality | Would removing this claim materially change the interpretation of the case? |
| Relevance | Does the evidence concern the entities, period, and event described by the claim? |
| Support | Does the evidence logically support the claim? |
| Sufficiency | Is the provided evidence set adequate for the scope and strength of the claim? |
| Necessity | Is this evidence required, or is it one of several acceptable alternatives? |
| Ambiguity | Are multiple evidence sets equally defensible? If yes, all acceptable sets should be represented, not collapsed into one. |

## Annotation questions (worked form)

The same six decisions, restated as the concrete questions an annotator walks through for each candidate claim:

1. Is this a material claim requiring evidence? (Not every sentence in a report is a checkable factual assertion.) — **Materiality**
2. Which records directly support it? — **Support**
3. Which records are necessary but not individually sufficient? (E.g. one leg of a three-hop transfer chain.) — **Necessity**
4. Are multiple evidence sets valid? (Two different, non-overlapping sets of records could each independently establish the same claim.) — **Ambiguity**
5. What constitutes minimum sufficient evidence for this claim? — **Sufficiency**
6. Is the claim too broad for the available evidence? (A claim can be true but not fully supportable by what the case actually contains.) — **Sufficiency**
7. Are there ambiguous but reasonable alternative mappings? (Two annotators could legitimately disagree on which record a vague claim refers to.) — **Relevance / Ambiguity**

## Annotation provenance

Every annotation package records:

- annotator ID/pseudonym;
- annotation version;
- task version;
- dataset version/hash;
- date;
- adjudication status (draft / single-annotator / adjudicated / multi-annotator-validated);
- disagreement notes where applicable.

`evidence-annotations.json` today records only a subset of this (task/dataset identity, the flat gold evidence id list) — extending it to the full provenance record above is planned, not yet done.

## Multi-annotator validation

**Not yet performed.** The plan is independent annotation by more than one evaluator where feasible, ideally including AML/compliance domain expertise, with inter-rater agreement recorded using a statistic appropriate to the annotation structure and number of raters — Cohen's kappa (two raters, categorical), Fleiss' kappa (three or more raters, categorical), or Krippendorff's alpha (mixed/missing data) — rather than one statistic hard-coded regardless of fit.

The tooling to *support* this once real annotations exist already ships: `src/AmlAgent.Evidence/HumanAnnotation.cs` (schema + loader) and `src/AmlAgent.Evidence/JudgeVsHumanComparison.cs` (raw confusion-matrix comparison, including `CompareAnnotators` for inter-annotator agreement) — see `tests/AmlAgent.ResearchValidation/HumanAnnotationTests.cs` for how they're exercised today, against a fixture explicitly marked synthetic, never against fabricated "real" annotations. No premature validity statistic (e.g. Kappa) is computed by that tooling yet, deliberately — see its own doc comments.

## Multiple-valid-gold handling

The protocol does not assume every claim has one unique correct evidence set. The annotation data model should be able to represent:

- **mandatory evidence** — must be cited for the claim to count as supported;
- **acceptable alternative evidence** — one of several equally-valid ways to support the claim;
- **optional corroborating evidence** — strengthens but is not required for the claim to be considered supported;
- **minimum sufficient evidence combinations** — the smallest sets of records that jointly satisfy sufficiency.

This is a meaningful improvement over flat set comparison (today's `gold_evidence_txn_ids: [...]` list, which implicitly treats every gold id as equally mandatory) and is reflected in the Phase C claim-level schema sketched in [docs/research-scope-mapping.md](research-scope-mapping.md#planned-claim-level-schema) — not yet implemented in the running benchmark.
