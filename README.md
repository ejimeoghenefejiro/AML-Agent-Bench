# AML-Agent-Bench

> A domain-specific benchmark for evaluating whether agentic AI systems can perform regulated FinTech reasoning — AML network analysis, transaction clustering, suspicious-flow detection, temporal anomaly detection and evidence-based, compliance-friendly reporting.

**Status:** active PhD research codebase. C# / .NET 8 end-to-end, Microsoft Semantic Kernel as the agent core, polyglot Docker harness so non-C# agents can also be benchmarked, dual deterministic + LLM-as-judge evaluation, first cross-model and cross-language preliminary results captured.

---

## Abstract

> ### Evaluating Agentic AI for Anti-Money Laundering Compliance: The AML-Agent-Bench Framework
>
> Agentic AI systems built on large language models are increasingly proposed for automating financial-crime compliance tasks such as anti-money laundering (AML) investigation. Existing agent benchmarks, however, target general coding or reasoning and do not assess the capabilities that regulated FinTech work demands: graph-based transaction-network analysis, temporal anomaly detection, evidence-based risk scoring, and the production of compliance-friendly, auditable text.
>
> This thesis introduces **AML-Agent-Bench**, an open-source benchmark suite designed specifically for evaluating agentic AI on AML reasoning. The framework couples deterministic structural assessment with LLM-as-judge qualitative scoring, so that an agent must be both numerically correct and write evidence-cited, regulator-friendly output to pass. The current suite contains two tasks: a static graph-clustering and risk-scoring task over a synthetic transaction ledger, and a temporal anomaly-detection task in which agents must reason about how a transaction network changes over three calendar weeks and produce a compliance-style report that cites transaction IDs and separates observed facts from analytical interpretation.
>
> Methodologically, the benchmark contributes a reusable evaluation architecture. A reference agent implemented in C# with Microsoft Semantic Kernel acts both as the primary subject of investigation and as the LLM-as-judge that scores other agents along six regulatory dimensions (evidence citation, temporal reasoning, anomaly detection, fact-versus-assumption separation, compliance tone, absence of unsupported claims). A language-agnostic Docker harness enables any agent — packaged as a folder, a pre-built image, or a user submission — to be benchmarked against identical tasks, supporting cross-language comparison.
>
> The framework addresses three research questions:
> 1. Can current agentic AI systems perform AML reasoning to the standards required for regulatory deployment?
> 2. What failure modes emerge under the dual pressure of structural correctness and compliance-style writing?
> 3. How can an LLM-as-judge be made trustworthy enough to grade regulatory output?
>
> Beyond the benchmark itself, contributions include the reproducible task design, the dual-evaluator methodology, and the C# / Semantic Kernel reference architecture for trustworthy compliance-oriented agents. By focusing evaluation on the specific reasoning a financial-crime analyst would need to defend in front of a regulator, AML-Agent-Bench establishes a foundation for measuring — and ultimately improving — agentic AI in high-stakes RegTech settings.

**Keywords:** agentic AI · large language models · anti-money laundering · benchmark · LLM-as-judge · Semantic Kernel · RegTech · compliance · graph reasoning · temporal anomaly detection

> Canonical standalone copy of the abstract (with BibTeX): [docs/abstract.md](docs/abstract.md).
>
> First cross-model and cross-language results: [docs/preliminary-results.md](docs/preliminary-results.md). Demo script for supervisor meetings: [docs/demo-script.md](docs/demo-script.md). One-button reproducer for both with-Docker and without-Docker runs: [docs/reproduce.md](docs/reproduce.md).

---

## Table of contents

0. [Abstract](#abstract)
1. [Research problem](#1-research-problem)
2. [What's in the box](#2-whats-in-the-box)
3. [Prerequisites](#3-prerequisites)
4. [Quick start (one button, with or without Docker)](#4-quick-start-one-button-with-or-without-docker)
5. [Running with a real LLM](#5-running-with-a-real-llm)
6. [Benchmarking your own agent](#6-benchmarking-your-own-agent)
7. [Tasks](#7-tasks)
8. [The two evaluators (xUnit + SK judge)](#8-the-two-evaluators-xunit--sk-judge)
9. [Results: the bench_result.json contract](#9-results-the-bench_resultjson-contract)
10. [Repository structure](#10-repository-structure)
11. [Architecture](#11-architecture)
12. [CLI reference](#12-cli-reference)
13. [Troubleshooting](#13-troubleshooting)
14. [Costs and safety notes](#14-costs-and-safety-notes)
15. [Reproducing the data](#15-reproducing-the-data)
16. [License & data disclaimer](#16-license--data-disclaimer)

---

## 1. Research problem

> Existing agent benchmarks test coding or general reasoning, but they do not sufficiently evaluate whether AI agents can perform regulated FinTech reasoning tasks such as AML network analysis, transaction clustering, suspicious-flow detection, and evidence-based risk scoring.

Concretely, AML-Agent-Bench tests whether an agent can:

- read structured transaction data and build a **directed weighted transaction graph**
- identify connected account clusters and circular flows
- detect **temporal change** in a transaction network (week-over-week, not just static)
- compute composite **risk scores** under explicit rules
- **cite evidence by transaction ID** when writing about suspicious activity
- **separate observed facts from analytical assumptions**
- use **regulator-friendly, compliance-aware language**
- avoid hallucinated transactions, accounts or amounts
- produce **deterministic, schema-conformant outputs** that can be re-validated

A second goal is methodological: **make this evaluation reproducible and language-agnostic** so different agent implementations (C# / Python / TypeScript / Go) can be compared on identical tasks.

See [docs/research-problem.md](docs/research-problem.md) for the longer write-up.

---

## 2. What's in the box

### The four projects, in plain English

| Project | What it is | Exam analogy |
|---|---|---|
| **AmlAgent** | The AI agent that does the AML task (and also acts as the LLM judge that grades the report). | The **candidate** sitting the exam — and a second reviewer who grades the written answer. |
| **AmlAgent.Harness** | The runner: builds everything, runs the agent, scores it, writes results. | The **invigilator** — runs the exam and marks the candidate. |
| **AmlAgent.Oracle** | A hand-written correct answer used to sanity-check the bench. | The **marking scheme** — the known-correct answers, used to prove the bench itself works. |
| **AmlAgent.Tests** | Rule-based tests (xUnit) that check the agent's output is correct. | The **rubric** — the deterministic rules every answer must satisfy. |

**Put together:** the **Harness** (invigilator) gives the **Agent** (candidate) the AML task, then marks its output using the **Tests** (rubric) and the **Agent-as-judge** (reviewer). The **Oracle** (marking scheme) is the gold-standard answer used to prove the marking process itself is sound.

### Full layer map

| Layer | Implementation | Purpose |
|---|---|---|
| **Reference agent** | `agents/csharp-sk/` — .NET 8 + Microsoft Semantic Kernel | The primary subject of the PhD investigation; tool-calling agent with `run`, `chat`, and `judge` subcommands |
| **Reference oracle** | `src/AmlAgent.Oracle/` | Pure-C# canonical solution for Task 001; produces ground-truth output without spending LLM tokens |
| **Harness** | `src/AmlAgent.Harness/` | Benchmark runner; supports `--local` (no Docker) and Docker modes; writes consolidated `bench_result.json` per run |
| **LLM-as-judge** | `agents/csharp-sk/Agent/JudgeAgent.cs` | The same SK core grades qualitative regulatory properties against a `rubric.json` |
| **Tests** | `tests/AmlAgent.Tests/` (xUnit) | Deterministic schema / range / sort / citation assertions across two tasks and the judge report |
| **Tasks** | `tasks/<task-id>/` | Self-contained task definitions: brief + data + expected behaviour + tests + rubric |
| **Submissions** | `submissions/` (mostly gitignored) | Drop point for external agents; ships with one reference Python baseline |
| **Run results** | `results/` (gitignored JSONs, tracked README) | One `bench_result.json` per run, ready for cross-run aggregation |

Everything in the runtime path is **C#**. Python is retained only for the one-off synthetic-data generator (`scripts/generate_synthetic_aml_data.py`) used to produce Task 001's dataset, and for the reference Python baseline submission (`submissions/python-baseline/`).

---

## 3. Prerequisites

To pull and run this repo you need:

| Tool | Version | Required for |
|---|---|---|
| **Git** | any recent | Cloning the repo |
| **.NET SDK** | **8.0.x** (newer also works) | Building + running everything |
| **PowerShell** | 5.1 or 7+ | The one-button reproducer script on Windows (Bash equivalent ships for Linux/macOS/WSL) |
| **Docker Desktop** (or Docker Engine) | any recent | **Optional** — Docker mode. Use `--local` to run the C# agent on the host instead. |
| **OpenAI API key** | a working key | **Optional** — only needed for the live agent, the chat REPL, and the SK-as-judge. The Oracle path needs no key. |
| **Visual Studio 2022** | 17.8+ | Optional — for IDE work. The `dotnet` CLI alone is enough. |

Verify versions:

```cmd
git --version
dotnet --version
docker --version
```

The codebase has been tested on Windows 11 with PowerShell and `cmd.exe`. The code itself is cross-platform — `ShellTool` chooses `cmd` on Windows and `bash` on Linux/macOS.

---

## 4. Quick start (one button, with or without Docker)

### The fastest path — one PowerShell command

After cloning the repo and creating your `.env` (see §5.1 below), run:

```powershell
:: Local mode — no Docker required, ~30s, ~$0.003 in API cost
powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1 -Mode local

:: Docker mode — full polyglot path, includes Python baseline, ~2min
powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1 -Mode docker

:: Both modes back-to-back
powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1 -Mode both
```

Linux / macOS / WSL:

```bash
scripts/test-bench.sh --mode local
scripts/test-bench.sh --mode docker
scripts/test-bench.sh --mode both
```

The script runs build, the reference Oracle, the live C# / Semantic Kernel agent (always), and (Docker only) the Python baseline submission. Each step prints `PASS` or `FAIL` and ends with a coloured summary. See [docs/reproduce.md](docs/reproduce.md) for the full breakdown of what every PASS / FAIL outcome means.

### Cost-free sanity check (no API key, no Docker)

This sequence builds everything and runs the full reference pipeline without calling an LLM or starting Docker — perfect for confirming the bench installs cleanly:

```cmd
:: 1. clone
git clone https://github.com/ejimeoghenefejiro/AML-Agent-Bench.git
cd AML-Agent-Bench

:: 2. build all 4 projects
dotnet build AML-Agent-Bench.sln

:: 3. run xUnit (the in-process Oracle smoke tests run; workspace-dependent ones skip)
dotnet test tests\AmlAgent.Tests\AmlAgent.Tests.csproj

:: 4. run the C# reference oracle on Task 001's bundled data
dotnet run --project src\AmlAgent.Oracle -- ^
    --input tasks\aml-transaction-network\environment\data\transfers.csv ^
    --output aml_clusters.csv

:: 5. run the harness in oracle mode — stages a workspace, runs the oracle,
::    runs xUnit against it, writes bench_result.json, reports OVERALL PASS/FAIL
dotnet run --project src\AmlAgent.Harness -- --oracle --no-judge
```

Expected after step 5:

```text
[harness] wrote     <temp>\bench_result.json
[harness] archived  results\<timestamp>-...-AmlAgent.Oracle.json
[harness] OVERALL: PASS (xunit=0 judge=0)
```

To open everything in Visual Studio 2022:

```cmd
start AML-Agent-Bench.sln
```

---

## 5. Running with a real LLM

### 5.1 Configure your API key in a `.env`

Both the C# agent and the harness load a `.env` from the repo root on startup (via `agents/csharp-sk/Config/DotEnv.cs` and `src/AmlAgent.Harness/DotEnv.cs`). Create it:

```cmd
copy .env.example .env
notepad .env
```

Set the line:

```text
OPENAI_API_KEY=sk-your-key-here
```

`.env` is in `.gitignore` — it will never be committed.

### 5.2 Interactive CMD chat (cheapest way to sanity-check the agent)

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat
```

You'll see:

```text
[env] loaded C:\PHD\AML-Agent-Bench\AML-Agent-Bench\.env
============================================================
 AmlAgent — interactive CMD chat (C# + Semantic Kernel)
 model:   gpt-4o-mini
 sandbox: <current directory>
 commands: /exit  /reset  /help
============================================================

you>
```

The agent has these tools wired in: `files.ListDir`, `files.ReadFile`, `files.WriteFile`, and `shell.Run`. To pre-load a task into the chat context:

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat --task aml-transaction-network
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat --task task-006-temporal-network-anomaly-detection
```

Slash commands: `/exit`, `/reset`, `/help`.

### 5.3 Full benchmark run — `--local` mode (no Docker)

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --agent csharp-sk ^
    --task task-006-temporal-network-anomaly-detection ^
    --local
```

The harness stages a workspace, runs the C# agent via `dotnet run` on the host, then runs the judge and xUnit and writes `bench_result.json`. No Docker required.

### 5.4 Full benchmark run — Docker mode

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --agent csharp-sk ^
    --task task-006-temporal-network-anomaly-detection
```

What the harness does:

1. Builds the agent's Docker image from `agents/csharp-sk/Dockerfile`.
2. Stages a temp workspace from `tasks/<task>/environment/` + the task brief.
3. Runs the agent container against `/app` with `OPENAI_API_KEY`, `BENCH_MODEL`, `BENCH_MAX_STEPS`.
4. Runs `aml-agent judge` against the workspace (if the task has `rubric.json`).
5. Runs `dotnet test` against the workspace via `AML_BENCH_WORKSPACE` (xUnit, with TRX logger).
6. Writes `bench_result.json` to the workspace + an archival copy to `results/`.
7. Prints `OVERALL: PASS / FAIL (xunit=… judge=…)`.

### 5.5 Just the SK-as-judge on an existing workspace

If you already have an agent's output files in a folder and want to re-score them:

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- ^
    judge ^
    --task task-006-temporal-network-anomaly-detection ^
    --workspace C:\path\to\workspace
```

This writes `judge_report.json` into that workspace and prints something like:

```text
[judge] overall: 29/30 = 96.7%
[judge] verdict: PASS (threshold 70%)
```

---

## 6. Benchmarking your own agent

The harness accepts **three** agent sources. Pick the one that matches how your agent is packaged.

### 6.1 As a folder inside `agents/`

Useful if you're co-developing the agent alongside the bench. With `--local` you can run it without Docker provided it's a C# `.csproj`; without `--local` it must ship a Dockerfile.

```text
agents/
└── my-agent/
    └── Dockerfile
```

```cmd
dotnet run --project src\AmlAgent.Harness -- --agent my-agent --task <task-id>
```

### 6.2 As a pre-built Docker image

Useful if you have an image on a registry (public or private):

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --agent-image my-registry/my-agent:v1 ^
    --task task-006-temporal-network-anomaly-detection
```

No build step happens — the harness `docker run`s the image directly.

### 6.3 As a local folder submission

Drop a folder under `submissions/` containing a `Dockerfile` (and any code/data you want baked in). `submissions/*` is gitignored by default so external code stays out of the repo.

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --submission submissions\my-python-agent ^
    --task task-006-temporal-network-anomaly-detection
```

### 6.4 Reference Python baseline

The repo ships a working Python agent at [submissions/python-baseline/](submissions/python-baseline/) — minimal ReAct loop using the OpenAI Python SDK with `list_dir` / `read_file` / `write_file` tools. Run it to verify the polyglot harness path works on your machine:

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --submission submissions\python-baseline ^
    --task task-006-temporal-network-anomaly-detection
```

### 6.5 Minimum agent contract

Whatever language you write your agent in, the image must:

1. Read `${BENCH_TASK_DIR:-/app}/instruction.md` (or `prompt.md`).
2. Use `/app` as its working directory; write all required output files there.
3. Honour these env vars passed by the harness:
   - `OPENAI_API_KEY` (or your own LLM credential)
   - `BENCH_MODEL`
   - `BENCH_MAX_STEPS`
   - `BENCH_TASK_DIR`
4. Exit `0` when it has produced its outputs.

The harness ignores the exit code for grading — **scoring is purely on the workspace contents**. The judge and xUnit run after the container exits.

See [submissions/README.md](submissions/README.md) for the full submission contract.

---

## 7. Tasks

| ID | Title | Difficulty | Outputs the agent must produce | Evaluators |
|---|---|---|---|---|
| `aml-transaction-network` | Static AML graph analysis and suspicious-cluster risk scoring | Hard | `aml_clusters.csv` | xUnit |
| `task-006-temporal-network-anomaly-detection` | Week-over-week anomaly detection with compliance-style reporting | Hard | `temporal_anomaly_summary.csv` + `temporal_anomaly_report.md` | xUnit + SK-as-judge |

### Task 001 — `aml-transaction-network`

- 154 synthetic transfers across multiple accounts.
- Agent must build a directed weighted graph, find connected clusters, detect circular flows, compute a composite risk score in `[0, 1]`, filter to `risk >= 0.65`, sort desc, round to 4 dp.
- See `tasks/aml-transaction-network/instruction.md`.

### Task 006 — `task-006-temporal-network-anomaly-detection`

- 32 synthetic transfers across **three calendar weeks**: baseline → new intermediary accounts spike → exit to high-risk jurisdictions.
- Agent must produce a structured per-week CSV **and** a compliance-style markdown report citing transaction IDs.
- The markdown report is graded by the SK-as-judge across six dimensions:
  `evidence_citation`, `temporal_reasoning`, `anomaly_detection`, `fact_vs_assumption`, `compliance_tone`, `avoids_unsupported_claims`.
- See `tasks/task-006-temporal-network-anomaly-detection/`:
  [prompt.md](tasks/task-006-temporal-network-anomaly-detection/prompt.md),
  [expected-behaviour.md](tasks/task-006-temporal-network-anomaly-detection/expected-behaviour.md),
  [tests.md](tasks/task-006-temporal-network-anomaly-detection/tests.md),
  [rubric.json](tasks/task-006-temporal-network-anomaly-detection/rubric.json).

More tasks can be added by creating a new `tasks/<id>/` folder with the same files.

**See [docs/preliminary-results.md](docs/preliminary-results.md) for first real cross-model and cross-language results across both tasks.**

---

## 8. The two evaluators (xUnit + SK judge)

After the agent exits, the harness runs **both** evaluators against the same workspace. Either failing produces `OVERALL: FAIL`. The judge runs first so the resulting `judge_report.json` is on disk before xUnit asserts on it.

### 8.1 `AmlAgent.Tests` (xUnit — deterministic)

Pure-C# tests in `tests/AmlAgent.Tests/`. Read `AML_BENCH_WORKSPACE` and validate the workspace's output files. They emit a TRX log that the harness parses into the consolidated `bench_result.json`. Examples of what they check:

- Schema matches **exactly** the expected column order.
- All `risk_score`s in `[0, 1]` and above the task threshold.
- Sort order: descending by `risk_score`, then ascending by `cluster_id`.
- Numeric rounding to 4 decimal places.
- For Task 006: three weeks present, strictly increasing `anomaly_score`, `week_3 >= 0.7`, ≥ 3 transaction-ID citations in the markdown.
- For the judge report: valid JSON, internal arithmetic correct, verdict = `PASS`.

Tests are gated on workspace shape: e.g. Task 001 tests skip if `data/transfers.csv` isn't present, so multi-task workspaces don't cross-fail.

### 8.2 SK-as-judge (LLM-graded, qualitative)

`aml-agent judge` loads a task's `rubric.json`, the agent's output file(s), and the underlying ground-truth data (so the judge can verify citations). It sends them to gpt-4o-mini through Semantic Kernel with `FunctionChoiceBehavior.None` and `ResponseFormat=json_object`, asking for structured scoring per dimension.

Example `judge_report.json` (real run, Task 006):

```json
{
  "scores": {
    "evidence_citation":         {"score": 5, "max": 5, "reasoning": "All claims are supported by specific transaction IDs from the dataset."},
    "temporal_reasoning":        {"score": 5, "max": 5, "reasoning": "The analysis effectively describes the changes in the network across the three weeks."},
    "anomaly_detection":         {"score": 5, "max": 5, "reasoning": "Correctly identifies week 2 as the start of anomalous activity and week 3 as the exit phase."},
    "fact_vs_assumption":        {"score": 4, "max": 5, "reasoning": "Mostly separates facts from assumptions, but could clarify further on implications of the data."},
    "compliance_tone":           {"score": 5, "max": 5, "reasoning": "Uses cautious language and avoids accusations of wrongdoing."},
    "avoids_unsupported_claims": {"score": 5, "max": 5, "reasoning": "No unsupported claims are made; all cited data exists in the provided dataset."}
  },
  "overall_score": 29,
  "overall_max": 30,
  "overall_percentage": 0.9667,
  "verdict": "PASS"
}
```

The overall percentage and verdict are **recomputed defensively in C#** from the per-dimension scores — the LLM cannot game the arithmetic.

---

## 9. Results: the `bench_result.json` contract

Every harness invocation writes a consolidated JSON record summarising the entire run. Two copies are written:

1. **In the workspace** — `<temp>/aml-bench-.../bench_result.json` (deleted unless `--keep-workspace`).
2. **In the repo's `results/` folder** — `results/<UTC-timestamp>-<task>-<agent>.json` (persists; gitignored so the folder doesn't bloat).

One file per run = trivial cross-run aggregation. See [results/README.md](results/README.md) for the full schema and a ready-made PowerShell / jq aggregator that turns the folder into a results table.

A trimmed example:

```json
{
  "task": "task-006-temporal-network-anomaly-detection",
  "agent": { "name": "csharp-sk", "model": "gpt-4o-mini", "mode": "local" },

  "agent_outputs": {
    "temporal_anomaly_summary.csv": {
      "rows": [
        { "week": "week_1", "anomaly_score": "0.0000", ... },
        { "week": "week_2", "anomaly_score": "0.5000", ... },
        { "week": "week_3", "anomaly_score": "1.0000", ... }
      ]
    }
  },

  "xunit":  { "verdict": "PASS", "total": 21, "passed": 15, "failed": 0, "skipped": 6, "failures": [] },
  "judge":  { "overall_percentage": 0.9667, "verdict": "PASS", "scores": { ... six dimensions ... } },

  "overall_verdict": "PASS",
  "overall_reason":  "xUnit PASS (15/21); judge PASS at 96.7%"
}
```

When the agent fails an assertion, the same file contains the **exact rule it broke** in `xunit.failures[*].message` — e.g. `"expected week_3 anomaly_score >= 0.7, got 0.375"`. That string is the empirical evidence that the bench discriminates between agents.

---

## 10. Repository structure

```text
AML-Agent-Bench/
├── AML-Agent-Bench.sln           # Visual Studio 2022 solution (4 projects)
├── .env.example                  # copy to .env locally; never committed
├── agents/
│   ├── README.md                 # polyglot agent contract
│   └── csharp-sk/                # PRIMARY PhD agent (.NET 8 + Semantic Kernel)
│       ├── AmlAgent.csproj
│       ├── Program.cs            # subcommands: run | chat | judge
│       ├── Agent/
│       │   ├── KernelFactory.cs  # shared SK kernel + plugin wiring
│       │   ├── BenchmarkAgent.cs # `run` — one-shot benchmark loop
│       │   ├── ChatAgent.cs      # `chat` — interactive CMD REPL
│       │   └── JudgeAgent.cs     # `judge` — LLM-as-judge rubric scoring
│       ├── Config/
│       │   └── DotEnv.cs         # local .env loader
│       ├── Tools/
│       │   ├── FileTools.cs      # ListDir / ReadFile / WriteFile
│       │   └── ShellTool.cs      # cross-platform shell (cmd / bash)
│       ├── Dockerfile            # sandbox: .NET SDK + dotnet-script
│       ├── .dockerignore         # keeps host bin/obj/ out of the build context
│       └── README.md
├── src/
│   ├── AmlAgent.Oracle/          # reference Oracle (no LLM, no Docker)
│   │   ├── AmlGraph.cs           # union-find WCC + iterative Tarjan SCC
│   │   ├── OracleRunner.cs       # canonical clustering pipeline
│   │   └── Program.cs            # `aml-oracle --input ... --output ...`
│   └── AmlAgent.Harness/         # benchmark runner (local + Docker)
│       ├── Program.cs            # `aml-harness --agent / --agent-image / --submission / --local`
│       ├── ReportBuilder.cs      # writes consolidated bench_result.json + results/ archive
│       └── DotEnv.cs             # mirror loader so the harness also picks up .env
├── tests/
│   └── AmlAgent.Tests/           # xUnit
│       ├── OracleSmokeTests.cs       # in-process Oracle tests
│       ├── OutputContractTests.cs    # Task 001 schema / range / sort tests
│       ├── Task006SummaryTests.cs    # Task 006 CSV + markdown tests
│       └── JudgeReportTests.cs       # judge_report.json shape + verdict
├── tasks/
│   ├── aml-transaction-network/
│   │   ├── instruction.md
│   │   ├── task.toml
│   │   └── environment/data/transfers.csv
│   └── task-006-temporal-network-anomaly-detection/
│       ├── README.md                  # task overview
│       ├── prompt.md                  # canonical brief
│       ├── instruction.md             # alias pointer
│       ├── expected-behaviour.md      # what a good response looks like
│       ├── tests.md                   # description of the test plan
│       ├── rubric.json                # SK-as-judge scoring criteria
│       ├── task.toml
│       └── environment/data/weekly_transfers.csv
├── submissions/                       # drop external agents here (gitignored)
│   ├── README.md
│   └── python-baseline/               # reference Python agent (committed)
│       ├── Dockerfile
│       ├── agent.py
│       ├── README.md
│       └── .dockerignore
├── results/                           # per-run bench_result.json archive (gitignored)
│   └── README.md                      # schema + aggregation one-liners
├── scripts/
│   ├── test-bench.ps1                 # one-button reproducer (Windows)
│   ├── test-bench.sh                  # one-button reproducer (Linux/macOS/WSL)
│   └── generate_synthetic_aml_data.py # one-off Task 001 data generator
├── docs/
│   ├── abstract.md                    # canonical citable abstract + BibTeX
│   ├── preliminary-results.md         # first cross-model and cross-language results
│   ├── demo-script.md                 # rehearsed supervisor-meeting demo
│   ├── reproduce.md                   # how to reproduce results locally
│   └── research-problem.md            # extended research framing
└── README.md                          # this file
```

---

## 11. Architecture

```text
                ┌────────────────────────────────────────────────────────┐
                │             aml-harness (src/AmlAgent.Harness)         │
                │   --local OR --agent-image / --submission / --agent    │
                │                                                        │
                │   stages workspace ─► runs agent ─► runs two evaluators│
                │   from tasks/<id>/    (host or                         │
                │                       Docker)         ─► bench_result  │
                │                                          (workspace +  │
                │                                          results/)     │
                └─────────────┬────────────────────┬─────────────────────┘
                              │                    │
            ┌─────────────────▼────┐   ┌──────────▼──────────────────────┐
            │  Agent                │   │  Evaluators (host)              │
            │  any language         │   │                                  │
            │                       │   │  1. aml-agent judge              │
            │  primary:             │   │     SK chat against rubric.json  │
            │  C# + Semantic        │   │     → writes judge_report.json   │
            │  Kernel               │   │                                  │
            │                       │   │  2. AmlAgent.Tests (xUnit)       │
            │  reads /app/          │   │     deterministic structure      │
            │  instruction.md       │   │     → TRX log                    │
            │  writes /app/<out>    │   │                                  │
            │                       │   │  → OVERALL: PASS / FAIL          │
            └───────────────────────┘   └──────────────────────────────────┘
```

**Key design choices**

- **Agent core is Semantic Kernel.** Tools are exposed as `KernelFunction`s; the LLM drives an auto function-calling loop until it emits `DONE`. The agent never has direct access to the test code or the rubric.
- **Two evaluators with the right ordering.** Judge runs first so its JSON is on disk; xUnit then asserts on both the agent's outputs **and** the judge report. Either failing produces `OVERALL: FAIL`.
- **C# does the arithmetic.** The judge LLM produces per-dimension scores; the harness recomputes the overall percentage and verdict in C#, so the LLM cannot inflate its own pass rate.
- **The judge is grounded.** The judge prompt includes the task's underlying data so the LLM can verify cited transaction IDs really exist.
- **Workspace isolation.** Each run creates a fresh temp workspace; no two runs share state.
- **Local mode parity.** `--local` runs the C# agent on the host via `dotnet run` and runs the same evaluators against the same workspace shape — useful when Docker isn't available.
- **Polyglot harness.** The harness only requires a Docker image that reads `instruction.md` and writes output files — the agent language doesn't matter. The bundled Python baseline proves this on every Docker-mode run.
- **Persistent results.** Every run produces a `bench_result.json` and an archival copy under `results/` for cross-run aggregation.

---

## 12. CLI reference

### `aml-agent` (the C# / SK agent)

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- <command> [options]
```

| Command | What it does |
|---|---|
| `run` | Benchmark loop. Reads `instruction.md` (or `prompt.md`) from `$BENCH_TASK_DIR` and runs the SK tool-calling loop until the model emits `DONE` or `BENCH_MAX_STEPS` is exceeded. |
| `chat [--task <id>]` | Interactive CMD REPL. With `--task`, pre-loads that task's instructions into context. Slash commands: `/exit`, `/reset`, `/help`. |
| `judge --task <id> --workspace <path> [--rubric <path>]` | LLM-as-judge mode. Scores the workspace against the task's `rubric.json`, writes `judge_report.json`, exits 0 on PASS / 1 on FAIL. |
| `help` | Print usage. |

Environment:

| Var | Default | Meaning |
|---|---|---|
| `OPENAI_API_KEY` | (required) | Loaded from `.env` if present |
| `BENCH_MODEL` | `gpt-4o-mini` | Chat completion model |
| `BENCH_JUDGE_MODEL` | falls back to `BENCH_MODEL` | Lets you use a different model for judging |
| `BENCH_TASK_DIR` | `/app` (`run`) / cwd (`chat`) | Sandbox root |
| `BENCH_MAX_STEPS` | `25` | Cap on agent turns in `run` mode |

### `aml-harness` (the benchmark runner)

```cmd
dotnet run --project src\AmlAgent.Harness -- [agent-source] [--task <id>] [options]
```

| Option | Description |
|---|---|
| `--agent <name>` | Subfolder of `agents/` (default `csharp-sk`). The harness builds its `Dockerfile`. |
| `--agent-image <tag>` | Use a **pre-built** Docker image as the agent (no build step). |
| `--submission <path>` | Build the `Dockerfile` in a local folder (typically `submissions/<name>`). |
| `--local` | Run the in-repo C# agent directly via `dotnet run` (no Docker). Cannot combine with `--agent-image` / `--submission`. |
| `--task <id>` | Task folder under `tasks/`. Default: `aml-transaction-network`. |
| `--model <id>` | Override `BENCH_MODEL`. |
| `--max-steps <n>` | Override `BENCH_MAX_STEPS`. |
| `--oracle` | Skip the agent; produce output via `AmlAgent.Oracle`. Only valid for `aml-transaction-network`. |
| `--no-judge` | Skip the LLM-as-judge stage even if `rubric.json` exists. |
| `--keep-workspace` | Don't delete the temp workspace on exit (useful for inspection). |

### `aml-oracle` (the reference solver — no LLM, no Docker)

```cmd
dotnet run --project src\AmlAgent.Oracle -- --input <transfers.csv> --output <aml_clusters.csv>
```

### `scripts/test-bench.ps1` and `scripts/test-bench.sh` (one-button reproducer)

```powershell
scripts\test-bench.ps1 -Mode local | docker | both [-Model <id>] [-MaxSteps <n>] [-SkipPython]
scripts/test-bench.sh   --mode local|docker|both [--model <id>] [--max-steps <n>] [--skip-python]
```

See [docs/reproduce.md](docs/reproduce.md) for the full reference.

---

## 13. Troubleshooting

**`dotnet --version` says something else** — install the .NET 8 SDK from <https://dotnet.microsoft.com/download/dotnet/8.0>. Newer SDKs (9.x) also work.

**`OPENAI_API_KEY is not set`** — copy `.env.example` to `.env` and put your key in. Make sure you're running commands from the repo root so the `.env` walk-up finder picks it up. Both the agent and the harness load `.env` independently.

**`docker: command not found` or daemon not running** — install / start Docker Desktop, OR add `--local` to the harness command, OR use `--oracle --no-judge` to verify the bench end-to-end without Docker or an LLM.

**Tests are mostly `[SKIP]`** — that's expected when you run `dotnet test` without a workspace. Workspace-dependent tests skip via `SkippableFact`. Run them through `aml-harness` (which sets `AML_BENCH_WORKSPACE`) to exercise them.

**`HTTP 429 (tokens: rate_limit_exceeded)`** — OpenAI per-organization TPM limits. Wait 60 s and re-run, drop `--max-steps`, or use a smaller model.

**Agent says `DONE` immediately without doing anything** — the system prompt biases toward `DONE` as a completion signal. In `chat` mode, give it a concrete instruction that asks for tool calls. In `run` mode the instruction.md gives it real work to do, so this is rarely an issue.

**Judge returns invalid JSON** — `JudgeAgent.cs` prints the raw response and exits non-zero. Re-run; if it persists, set `BENCH_JUDGE_MODEL=gpt-4o`.

**Agent FAILs `AnomalyScoreStrictlyIncreasing` (Task 006)** — the LLM frequently writes `week_3 anomaly_score < 0.7` for Task 006. This is **the bench discriminating against a real failure mode**, not a bug. Re-run; the same agent passes some of the time. See [docs/reproduce.md](docs/reproduce.md) for the full "what every FAIL means" table.

**Want to see what the agent produced before tests/judge run** — pass `--keep-workspace` to the harness; it prints the workspace path and leaves it on disk. The same data is also in `results/<timestamp>-...json` regardless.

**Docker build fails with NuGet path errors** — make sure `agents/csharp-sk/.dockerignore` is present (ships with the repo). It excludes host `bin/obj/` directories whose `project.assets.json` carries hardcoded Windows NuGet paths that break Linux container builds.

---

## 14. Costs and safety notes

- The C# **oracle** and **xUnit tests** cost nothing. You can fully verify the bench's mechanics without an API key (`--oracle --no-judge`).
- The **judge** uses gpt-4o-mini by default. Each judge call costs roughly **$0.001–0.005** depending on how long the candidate's report is.
- A full **agent benchmark run** (Task 006, `gpt-4o-mini`, default 25 steps) typically costs **a few cents**. Increase `--max-steps` carefully.
- The one-button reproducer in `local` mode costs **~$0.003 per run**; `docker` mode adds the Python baseline for **~$0.005 total per run**.
- Your API key is read from `.env`, which is in `.gitignore`. It is never written to logs, test artefacts, workspace files, or `results/` JSONs by this code.
- The bundled datasets are **synthetic**. No real customer data is present.

---

## 15. Reproducing the data

### Task 001 — `tasks/aml-transaction-network/environment/data/transfers.csv`

```cmd
python scripts\generate_synthetic_aml_data.py
```

The only Python file in the data-generation path. Deterministic (`random.seed(42)`).

### Task 006 — `tasks/task-006-temporal-network-anomaly-detection/environment/data/weekly_transfers.csv`

Hand-crafted to be deterministic and small. Commit it directly; no generator script is required.

---

## 16. License & data disclaimer

Source code: see [LICENSE](LICENSE).

The datasets in `tasks/*/environment/data/` are **synthetic** and intended only for research and evaluation. They do not represent real customer accounts, real transfers, or real SAR cases. Any resemblance to actual financial activity is coincidental.

---

## Pulling the latest

```cmd
git pull
dotnet build AML-Agent-Bench.sln
```

If new task folders appear they pick up automatically — the harness discovers tasks by directory name under `tasks/`. New xUnit test classes also pick up automatically when they're under `tests/AmlAgent.Tests/`.

## Getting help

- For the C# / SK agent specifics, see [agents/csharp-sk/README.md](agents/csharp-sk/README.md).
- For benching external agents, see [submissions/README.md](submissions/README.md).
- For the Python baseline submission, see [submissions/python-baseline/README.md](submissions/python-baseline/README.md).
- For the per-run result format, see [results/README.md](results/README.md).
- For the research framing, see [docs/research-problem.md](docs/research-problem.md).
- For reproducer flow, see [docs/reproduce.md](docs/reproduce.md).
- For first cross-model and cross-language data, see [docs/preliminary-results.md](docs/preliminary-results.md).
- For the rehearsed supervisor demo, see [docs/demo-script.md](docs/demo-script.md).
- File an issue on the GitHub repo with the workspace path (`--keep-workspace`) or the relevant `results/<...>.json` and the `OVERALL` line from the harness.
