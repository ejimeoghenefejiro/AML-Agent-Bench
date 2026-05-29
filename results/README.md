# results/

Every run of `aml-harness` writes a consolidated `bench_result.json` here, in
addition to the copy left in the temp workspace.

## File naming

```text
results/<UTC-timestamp>-<task>-<agent>.json

e.g. results/20260529-184223-task-006-temporal-network-anomaly-detection-csharp-sk.json
```

## What's in each file

```json
{
  "schema_version": "1.0",
  "run_id":        "...",
  "started_at_utc":"...",
  "completed_at_utc":"...",
  "elapsed_seconds": 15.3,
  "task":   "task-006-temporal-network-anomaly-detection",
  "agent":  { "source": "in-repo-local", "name": "csharp-sk",
              "model":  "gpt-4o-mini",   "max_steps": 12, "mode": "local" },
  "workspace":     "C:\\Users\\...\\Temp\\aml-bench-...",
  "agent_exit_code": 0,

  "agent_outputs": {
    "temporal_anomaly_summary.csv": {
      "size_bytes": 306,
      "rows": [
        { "week": "week_1", "anomaly_score": 0.0,    ... },
        { "week": "week_2", "anomaly_score": 0.1875, ... },
        { "week": "week_3", "anomaly_score": 0.375,  ... }
      ]
    },
    "temporal_anomaly_report.md": {
      "size_bytes": 1657,
      "content_preview": "# Executive Summary\n...",
      "citation_count": 6
    }
  },

  "xunit": {
    "exit_code": 1,
    "verdict":   "FAIL",
    "total": 21, "passed": 14, "failed": 1, "skipped": 6,
    "failures": [
      {
        "test_name": "AmlAgent.Tests.Task006SummaryTests.AnomalyScoreStrictlyIncreasing",
        "message":   "expected week_3 anomaly_score >= 0.7, got 0.375"
      }
    ],
    "trx_present": true
  },

  "judge": {
    "scores": {
      "evidence_citation":         { "score": 4, "max": 5, "reasoning": "..." },
      "temporal_reasoning":        { "score": 5, "max": 5, "reasoning": "..." },
      "anomaly_detection":         { "score": 5, "max": 5, "reasoning": "..." },
      "fact_vs_assumption":        { "score": 4, "max": 5, "reasoning": "..." },
      "compliance_tone":           { "score": 5, "max": 5, "reasoning": "..." },
      "avoids_unsupported_claims": { "score": 4, "max": 5, "reasoning": "..." }
    },
    "overall_score": 27,
    "overall_max":   30,
    "overall_percentage": 0.9,
    "verdict": "PASS"
  },

  "overall_verdict": "FAIL",
  "overall_reason":  "xUnit FAIL (1 assertion(s)); judge PASS at 90.0%"
}
```

The most-PhD-relevant fields:

- `agent_outputs.temporal_anomaly_summary.csv.rows` — the **exact numbers the agent produced**
- `xunit.failures[*]` — the **exact rules the agent broke**
- `judge.scores` — the **per-dimension qualitative scores**
- `overall_reason` — one-line human summary that's perfect for tables

## Why a separate folder

- Workspace temp dirs are deleted unless `--keep-workspace` is set; this folder isn't.
- Per-run JSONs can be aggregated into a results table by a single jq / Python / pandas one-liner.
- Gitignored by default (see `.gitignore`) so you can run hundreds of evaluations without polluting the repo. Override per-file with `git add -f` if a specific run is worth committing as a citable artefact.

## Aggregating runs

PowerShell one-liner — table of every run in the folder:

```powershell
Get-ChildItem results\*.json | ForEach-Object {
    $r = Get-Content $_ -Raw | ConvertFrom-Json
    [pscustomobject]@{
        Run      = $_.Name
        Task     = $r.task
        Agent    = $r.agent.name
        Model    = $r.agent.model
        Mode     = $r.agent.mode
        Judge    = '{0:N1}%' -f ($r.judge.overall_percentage * 100)
        xUnit    = $r.xunit.verdict
        Verdict  = $r.overall_verdict
        Reason   = $r.overall_reason
    }
} | Format-Table -AutoSize
```

Bash / jq equivalent:

```bash
jq -r '[ .agent.name, .agent.model, .agent.mode,
         (.judge.overall_percentage * 100 | tostring + "%"),
         .xunit.verdict, .overall_verdict, .overall_reason ] | @tsv' results/*.json \
  | column -t -s $'\t'
```
