# task-007 — Multi-Source Mule Network Investigation

The reference task for the multi-source Data Adapter Layer: a single AML case whose
evidence is deliberately spread across **four heterogeneous systems**, merged into one
canonical case before the agent ever sees it.

| source | format | role |
|---|---|---|
| `transactions_primary.csv` | CSV | primary bank's own ledger |
| `transactions_correspondent.json` | JSON | correspondent bank's export (overlaps + adds the exit leg) |
| `transactions_archive.parquet` | Parquet | data-warehouse archive (a genuine cross-source merge conflict lives here) |
| `relationships.graphml` | GraphML | entity/relationship graph, including an independent watchlist flag |

`environment/case-definition.json` names these four sources. `src/AmlAgent.Harness/Program.cs`'s
`StageCanonicalCaseIfPresent` step loads and merges them via `AmlAgent.Adapters` (`AdapterRegistry`,
`CaseLoader`, `CanonicalCaseMerger`, `EvidenceIntegrityValidator`) before the agent starts, and
writes `case_manifest.json` plus deduplicated `data/transactions.csv` / `data/relationships.json`
exports for the agent to actually read.

## The scenario

An elderly victim (`N100`) is socially engineered into an authorised-push-payment fraud. Funds
route through two freshly-opened first-layer mules (`M201`, `M202`), converge on an aggregator
account (`M301` — independently corroborated by a prior SAR from another institution), and exit
offshore (`EXT401`). Mixed into the same data, deliberately, are:

- **`N150`** — a real relationship-graph connection to a mule account, but the transaction predates
  the fraud and is an unrelated small recurring payment. Tests whether the agent reasons about
  *timing*, not just graph adjacency.
- **`N160`/`N170`** — no relationship-graph connection at all; they only co-occur in the same
  correspondent-bank export batch. Tests whether the agent conflates "present in the case data"
  with "part of the network."
- **A genuine cross-source merge conflict** on `T2-001`: the correspondent feed and the archive
  disagree on its timestamp by three days (a realistic settlement-date discrepancy), surfaced in
  `case_manifest.json`'s `merge_conflicts` and `evidence_integrity`.

## What the agent must produce

| file | format | evaluator |
|---|---|---|
| `mule_network_findings.csv` | CSV | xUnit (`Task007MuleNetworkFindingsTests`) |
| `mule_network_report.md` | Markdown | SK-as-judge (`rubric.json`) |

See [prompt.md](prompt.md) for the canonical task brief, [expected-behaviour.md](expected-behaviour.md)
for what a good response looks like, and [tests.md](tests.md) for the full test plan.

## Why this task matters for the PhD

Every prior task assumed one input file in one format. This task proves the full pipeline —
`Storage Format -> Data Adapter -> Canonical AML Schema -> Scenario/Task -> Harness -> Assurance` —
end to end: real CSV/JSON/Parquet/GraphML adapters, real cross-source merging with a real detected
conflict, real evidence-integrity validation, and a task that is provably independent of which
format any given piece of evidence originally arrived in. The innocent-account distractors make
"avoid false implication" a first-class, machine-checkable metric rather than a purely qualitative
judge dimension.
