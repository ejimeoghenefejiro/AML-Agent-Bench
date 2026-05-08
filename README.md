# AML-Agent-Bench

**AML-Agent-Bench** is a domain-specific benchmark for evaluating whether agentic AI systems can perform regulated FinTech reasoning tasks involving anti-money laundering (AML), transaction network analysis, suspicious flow detection, clustering, and evidence-based risk scoring.

## Research problem

Existing agent benchmarks mostly test general coding, tool use, or broad reasoning. They do not sufficiently evaluate whether AI agents can operate in regulated FinTech contexts where the task requires:

- graph-based transaction reasoning
- suspicious cluster detection
- deterministic financial data processing
- evidence-based risk scoring
- compliance-aware outputs
- reproducible terminal execution

This repository provides a focused benchmark artifact for evaluating AI agents on AML transaction-network tasks.

## Thesis positioning

A possible PhD framing:

> Existing agent benchmarks test coding or general reasoning, but they do not sufficiently evaluate whether AI agents can perform regulated FinTech reasoning tasks such as AML network analysis, transaction clustering, suspicious flow detection, and evidence-based risk scoring.

## Current task

| Task | Area | Difficulty |
|---|---|---|
| `aml-transaction-network` | AML graph analysis and suspicious cluster scoring | Hard |

## Repository structure

```text
AML-Agent-Bench/
├── README.md
├── docs/
│   └── research-problem.md
├── scripts/
│   └── generate_synthetic_aml_data.py
└── tasks/
    └── aml-transaction-network/
        ├── instruction.md
        ├── task.toml
        ├── environment/
        │   ├── Dockerfile
        │   └── data/
        │       └── transfers.csv
        ├── solution/
        │   ├── solve.sh
        │   └── solve.py
        └── tests/
            ├── test.sh
            └── test_outputs.py
```

## Running the reference solution locally

From the task directory:

```bash
cd tasks/aml-transaction-network
python solution/solve.py
python -m pytest tests/test_outputs.py
```

## Expected output

The agent must produce:

```text
/app/aml_clusters.csv
```

with the exact schema:

```text
cluster_id,account_count,total_value,circular_flow_score,risk_score
```

## Notes

The dataset is synthetic and is intended for research/evaluation only. It does not contain real customer or payment data.

