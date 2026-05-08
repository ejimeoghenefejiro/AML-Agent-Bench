# Research Problem

## Core claim

Existing agent benchmarks test coding or general reasoning, but they do not sufficiently evaluate whether AI agents can perform regulated FinTech reasoning tasks such as AML network analysis, transaction clustering, suspicious flow detection, and evidence-based risk scoring.

## Why this matters

Financial crime detection is not just a generic machine learning or coding problem. It requires regulated, auditable, and domain-specific reasoning. An agent must understand the unit of analysis, preserve transaction direction, detect suspicious movement patterns, and produce deterministic outputs that can be validated.

## Benchmark gap

General-purpose agent benchmarks usually test whether an AI system can write code, edit files, use a terminal, or answer reasoning tasks. These are useful, but they do not fully test:

1. Whether an agent can interpret financial transaction networks correctly.
2. Whether an agent can distinguish accounts, transactions, countries, and clusters.
3. Whether an agent can create evidence-based suspicious-flow outputs.
4. Whether an agent can avoid plausible but non-compliant outputs.
5. Whether an agent can produce deterministic files under strict schema and validation constraints.

## Proposed contribution

AML-Agent-Bench introduces a focused FinTech evaluation setting where an agent must perform graph-based AML reasoning in a terminal environment.

The benchmark evaluates the agent's ability to:

- read structured transaction data
- build directed weighted transaction graphs
- identify connected account clusters
- detect circular transaction flows
- compute suspicious risk scores
- produce exact CSV outputs
- satisfy deterministic validation tests

## Research questions

1. Can agentic AI systems correctly perform graph-based AML investigation tasks from terminal instructions?
2. What failure modes occur when agents process regulated FinTech reasoning tasks?
3. Do agents preserve the correct unit of analysis when moving from transactions to accounts to clusters?
4. Can benchmark-driven evaluation reveal weaknesses in agentic financial-crime reasoning?
5. How can AML-specific task design improve the assessment of agentic AI systems?

## Expected contribution to PhD

This benchmark can support a PhD contribution by combining:

- a novel domain-specific benchmark
- synthetic AML transaction datasets
- reproducible terminal-based tasks
- evaluation of agentic AI systems
- analysis of domain-specific failure modes
- evidence for responsible AI use in regulated FinTech


## Current deterministic oracle output

The starter task currently expects 4 high-risk AML clusters after applying the risk-score threshold of `risk_score >= 0.65`.

This is intentional: the benchmark evaluates whether the agent can filter only the suspicious clusters rather than outputting every transaction group.
