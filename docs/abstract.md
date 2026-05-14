# Evaluating Agentic AI for Anti-Money Laundering Compliance: The AML-Agent-Bench Framework

> **Thesis abstract.** Canonical, citable form. The same text appears in the
> repository [README](../README.md#abstract). When this document and the
> README diverge, **this file is authoritative**.

---

## Abstract

Agentic AI systems built on large language models are increasingly proposed for automating financial-crime compliance tasks such as anti-money laundering (AML) investigation. Existing agent benchmarks, however, target general coding or reasoning and do not assess the capabilities that regulated FinTech work demands: graph-based transaction-network analysis, temporal anomaly detection, evidence-based risk scoring, and the production of compliance-friendly, auditable text.

This thesis introduces **AML-Agent-Bench**, an open-source benchmark suite designed specifically for evaluating agentic AI on AML reasoning. The framework couples deterministic structural assessment with LLM-as-judge qualitative scoring, so that an agent must be both numerically correct and write evidence-cited, regulator-friendly output to pass. The current suite contains two tasks: a static graph-clustering and risk-scoring task over a synthetic transaction ledger, and a temporal anomaly-detection task in which agents must reason about how a transaction network changes over three calendar weeks and produce a compliance-style report that cites transaction IDs and separates observed facts from analytical interpretation.

Methodologically, the benchmark contributes a reusable evaluation architecture. A reference agent implemented in C# with Microsoft Semantic Kernel acts both as the primary subject of investigation and as the LLM-as-judge that scores other agents along six regulatory dimensions (evidence citation, temporal reasoning, anomaly detection, fact-versus-assumption separation, compliance tone, absence of unsupported claims). A language-agnostic Docker harness enables any agent — packaged as a folder, a pre-built image, or a user submission — to be benchmarked against identical tasks, supporting cross-language comparison.

The framework addresses three research questions:

1. Can current agentic AI systems perform AML reasoning to the standards required for regulatory deployment?
2. What failure modes emerge under the dual pressure of structural correctness and compliance-style writing?
3. How can an LLM-as-judge be made trustworthy enough to grade regulatory output?

Beyond the benchmark itself, contributions include the reproducible task design, the dual-evaluator methodology, and the C# / Semantic Kernel reference architecture for trustworthy compliance-oriented agents. By focusing evaluation on the specific reasoning a financial-crime analyst would need to defend in front of a regulator, AML-Agent-Bench establishes a foundation for measuring — and ultimately improving — agentic AI in high-stakes RegTech settings.

---

**Keywords:** agentic AI · large language models · anti-money laundering · benchmark · LLM-as-judge · Semantic Kernel · RegTech · compliance · graph reasoning · temporal anomaly detection

## Citation

If you reference this work, please cite as:

```bibtex
@misc{aml-agent-bench,
  author       = {Ejime Oghenefejiro},
  title        = {{AML-Agent-Bench}: Evaluating Agentic AI for Anti-Money Laundering Compliance},
  year         = {2026},
  howpublished = {\url{https://github.com/ejimeoghenefejiro/AML-Agent-Bench}},
  note         = {PhD research codebase}
}
```

## See also

- [README](../README.md) — pull, build and run instructions
- [docs/research-problem.md](research-problem.md) — extended motivation
- [tasks/](../tasks/) — current benchmark tasks
