# Sufficiency annotations

This directory is for REAL human evidence-sufficiency annotations, once they
exist, in the schema `AmlAgent.Evidence.SufficiencyAnnotationReader` parses
(see `src/AmlAgent.Evidence/SufficiencyAnnotation.cs`):

```json
{
  "case_id": "task-007-case-001",
  "output_id": "agent-output-003",
  "annotators": [
    {
      "annotator_id": "H01",
      "claim_sufficiency": [
        {
          "claim_id": "MC3",
          "sufficiency_label": "insufficient",
          "minimum_sufficient_evidence_sets": [["T1-003", "T1-004", "T2-001"]],
          "rationale": "Citing only T1-003 establishes the M201->M301 hop but not that M301 itself is the aggregator that then exits the funds -- the layering claim needs the onward T2-001 leg too."
        }
      ]
    }
  ]
}
```

**No real human sufficiency annotations have been collected yet.**
`example_synthetic_sufficiency_annotation.json` in this directory is a
synthetic, hand-authored TEST FIXTURE used only to exercise the schema loader
in `tests/AmlAgent.Tests/SufficiencyAnnotationTests.cs` -- it is explicitly
not, and must never be presented as, real annotator data.

**This is schema and fixtures only (fix #8) -- deliberately not wired to any
scored metric.** `evidence_sufficiency_rate` in `judge_report.json` /
`assurance_profile.json` stays an explicit `null` (see
`AmlAgent.Evidence.EvidenceTraceabilityProfileBuilder.Build`) until a real
annotation round exists and has been validated for inter-rater agreement
(see `docs/evidence-annotation-protocol.md#evidence-sufficiency-annotation-schema`
and `#multi-annotator-validation`). Claim Support Coverage (fix #7) was
implemented and wired live first, precisely because it needs no semantic
adequacy judgement beyond the LLM identifying which evidence ids a report
cites -- sufficiency is a genuinely harder, human-judgement-dependent
question, and the repository does not compute a number for it just because
the schema now exists to receive one.

When real annotations are collected, add them as their own files (e.g.
`task-007-case-001-agent-output-003.json`), matching the convention in
`validation/gold/human-annotations/README.md`.
