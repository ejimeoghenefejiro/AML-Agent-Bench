# Human annotations

This directory is for REAL human gold annotations, once they exist, in the
schema `AmlAgent.Evidence.HumanAnnotationReader` parses (see
`src/AmlAgent.Evidence/HumanAnnotation.cs`):

```json
{
  "case_id": "task-007-case-001",
  "output_id": "agent-output-003",
  "annotators": [
    {
      "annotator_id": "H01",
      "claims": [
        { "claim_id": "C1", "classification": "supported", "evidence_ids": ["T1001", "T1004"] }
      ],
      "rubric_scores": { "network_identification": 4, "evidence_grounding": 5 }
    }
  ]
}
```

`rubric_scores` is optional and additive to the research-validation instructions'
own example -- only present when an annotator also scored rubric dimensions, for
the judge-vs-human rubric-score comparison (item 9).

**No real human annotations have been collected yet.** `example_synthetic_annotation.json`
in this directory is a synthetic, hand-authored TEST FIXTURE used only to exercise
the loader and comparison tooling in `tests/AmlAgent.ResearchValidation/HumanAnnotationTests.cs`
-- it is explicitly not, and must never be presented as, real annotator data. When
real annotations are collected, add them as their own files (e.g.
`task-007-case-001-agent-output-003.json`) and the comparison tooling in
`AmlAgent.Evidence.JudgeVsHumanComparison` is ready to use against them.
