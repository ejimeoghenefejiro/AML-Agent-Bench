# AML-Agent-Bench

> A domain-specific benchmark for evaluating whether agentic AI systems can perform regulated FinTech reasoning — AML network analysis, transaction clustering, suspicious-flow detection, temporal anomaly detection and evidence-based, compliance-friendly reporting.

**Status:** active PhD research codebase. C# / .NET 8 end-to-end, Semantic Kernel as the agent core, polyglot Docker harness so non-C# agents can also be benchmarked.

---

## Table of contents

1. [Research problem](#1-research-problem)
2. [What's in the box](#2-whats-in-the-box)
3. [Prerequisites](#3-prerequisites)
4. [Quick start (5 minutes, no LLM cost)](#4-quick-start-5-minutes-no-llm-cost)
5. [Running with a real LLM](#5-running-with-a-real-llm)
6. [Benchmarking your own agent](#6-benchmarking-your-own-agent)
7. [Tasks](#7-tasks)
8. [The two evaluators (xUnit + SK judge)](#8-the-two-evaluators-xunit--sk-judge)
9. [Repository structure](#9-repository-structure)
10. [Architecture](#10-architecture)
11. [CLI reference](#11-cli-reference)
12. [Troubleshooting](#12-troubleshooting)
13. [Costs and safety notes](#13-costs-and-safety-notes)
14. [Reproducing the data](#14-reproducing-the-data)
15. [License & data disclaimer](#15-license--data-disclaimer)

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

| Layer | Implementation | Purpose |
|---|---|---|
| **Reference agent** | `agents/csharp-sk/` — .NET 8 + Microsoft Semantic Kernel | The primary subject of the PhD investigation; tool-calling agent with `run`, `chat`, and `judge` subcommands |
| **Reference oracle** | `src/AmlAgent.Oracle/` | Pure-C# canonical solution for Task 001; produces ground-truth output without spending LLM tokens |
| **Harness** | `src/AmlAgent.Harness/` | Docker-based benchmark runner; works with any agent image |
| **LLM-as-judge** | `agents/csharp-sk/Agent/JudgeAgent.cs` | The same SK core grades qualitative regulatory properties against a `rubric.json` |
| **Tests** | `tests/AmlAgent.Tests/` (xUnit) | Deterministic schema / range / sort / citation assertions |
| **Tasks** | `tasks/<task-id>/` | Self-contained task definitions: brief + data + expected behaviour + tests + rubric |
| **Submissions** | `submissions/` (gitignored) | Drop point for other people's agent code/images for benchmarking |

Everything in the runtime path is **C#**. Python is retained only for the one-off synthetic-data generator (`scripts/generate_synthetic_aml_data.py`) used to produce Task 001's dataset.

---

## 3. Prerequisites

To pull and run this repo you need:

| Tool | Version | Required for |
|---|---|---|
| **Git** | any recent | Cloning the repo |
| **.NET SDK** | **8.0.x** (newer also works) | Building + running everything |
| **Docker Desktop** (or Docker Engine) | any recent | Running the benchmark harness end-to-end. **Optional** — you can build, test and run the oracle without Docker. |
| **OpenAI API key** | a working key | The live agent, the chat REPL, and the SK-as-judge. **Optional** — you can verify the entire bench without it via `--oracle --no-judge`. |
| **Visual Studio 2022** | 17.8+ | Optional — for IDE work. The `dotnet` CLI alone is enough. |

Verify versions:

```cmd
git --version
dotnet --version
docker --version
```

The codebase has been tested on Windows 11 with PowerShell and `cmd.exe`. The code itself is cross-platform — `ShellTool` chooses `cmd` on Windows and `bash` on Linux/macOS.

---

## 4. Quick start (5 minutes, no LLM cost)

This sequence pulls the repo, builds everything, and runs the full reference pipeline **without** calling an LLM or starting Docker.

```cmd
:: 1. clone
git clone https://github.com/ejimeoghenefejiro/AML-Agent-Bench.git
cd AML-Agent-Bench

:: 2. build all 4 projects
dotnet build AML-Agent-Bench.sln

:: 3. run xUnit (Oracle smoke tests only — workspace-dependent ones skip)
dotnet test tests\AmlAgent.Tests\AmlAgent.Tests.csproj

:: 4. run the C# reference oracle on Task 001's bundled data
dotnet run --project src\AmlAgent.Oracle -- ^
    --input tasks\aml-transaction-network\environment\data\transfers.csv ^
    --output aml_clusters.csv

:: 5. run the harness in oracle mode — stages a workspace, runs the oracle,
::    runs xUnit against the workspace, reports OVERALL PASS/FAIL
dotnet run --project src\AmlAgent.Harness -- --oracle --no-judge
```

Expected after step 5:

```text
[harness] OVERALL: PASS (xunit=0 judge=0)
```

If that prints, the whole non-LLM half of the bench is working on your machine.

To open everything in Visual Studio 2022:

```cmd
start AML-Agent-Bench.sln
```

---

## 5. Running with a real LLM

### 5.1 Configure your API key in a `.env`

The C# agent loads a `.env` from the repo root on startup (`agents/csharp-sk/Config/DotEnv.cs`). Create it:

```cmd
copy .env.example .env
notepad .env
```

Set just the line:

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

Type a question and press Enter. The agent has these tools wired in: `files.ListDir`, `files.ReadFile`, `files.WriteFile`, and `shell.Run`. To pre-load a task into the chat context:

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat --task aml-transaction-network
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat --task task-006-temporal-network-anomaly-detection
```

Slash commands: `/exit`, `/reset`, `/help`.

### 5.3 Full benchmark run (requires Docker)

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --agent csharp-sk ^
    --task task-006-temporal-network-anomaly-detection
```

What the harness does:

1. Builds the agent's Docker image from `agents/csharp-sk/Dockerfile`.
2. Stages a temp workspace from `tasks/<task>/environment/` + the task brief.
3. Runs the agent container against `/app` with `OPENAI_API_KEY`, `BENCH_MODEL`, `BENCH_MAX_STEPS`.
4. Runs `dotnet test` against the workspace via `AML_BENCH_WORKSPACE` (the xUnit evaluator).
5. Runs `aml-agent judge` against the workspace if the task has `rubric.json` (the LLM-as-judge evaluator).
6. Prints `OVERALL: PASS / FAIL (xunit=… judge=…)`.

### 5.4 Just the SK-as-judge on an existing workspace

If you have an agent's output files in a folder and just want to score them:

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- ^
    judge ^
    --task task-006-temporal-network-anomaly-detection ^
    --workspace C:\path\to\workspace
```

This writes `judge_report.json` into that workspace and prints something like:

```text
[judge] overall: 30/30 = 100.0%
[judge] verdict: PASS (threshold 70%)
```

---

## 6. Benchmarking your own agent

The harness accepts **three** agent sources. Pick the one that matches how your agent is packaged.

### 6.1 As a folder inside `agents/`

Useful if you're co-developing the agent alongside the bench.

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

Drop a folder under `submissions/` containing a `Dockerfile` (and any code/data you want baked in). `submissions/*` is gitignored, so external code stays out of the repo.

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --submission submissions\my-python-agent ^
    --task task-006-temporal-network-anomaly-detection
```

### 6.4 Minimum agent contract

Whatever language you write your agent in, the image must:

1. Read `${BENCH_TASK_DIR:-/app}/instruction.md` (or `prompt.md`).
2. Use `/app` as its working directory; write all required output files there.
3. Honour these env vars passed by the harness:
   - `OPENAI_API_KEY` (or your own LLM credential)
   - `BENCH_MODEL`
   - `BENCH_MAX_STEPS`
   - `BENCH_TASK_DIR`
4. Exit `0` when it has produced its outputs.

The harness ignores the exit code for grading — **scoring is purely on the workspace contents**. xUnit and the judge run after the container exits.

See [submissions/README.md](submissions/README.md) for a Python example.

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

---

## 8. The two evaluators (xUnit + SK judge)

After the agent exits, the harness runs **both** evaluators against the same workspace. Either failing produces `OVERALL: FAIL`.

### 8.1 `AmlAgent.Tests` (xUnit — deterministic)

Pure-C# tests in `tests/AmlAgent.Tests/`. Read `AML_BENCH_WORKSPACE` and validate the workspace's output files. Examples of what they check:

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
    "fact_vs_assumption":        {"score": 5, "max": 5, "reasoning": "Clearly distinguishes between observed data and interpretations."},
    "compliance_tone":           {"score": 5, "max": 5, "reasoning": "Uses cautious language and avoids accusations of wrongdoing."},
    "avoids_unsupported_claims": {"score": 5, "max": 5, "reasoning": "No unsupported claims are made; all cited data exists in the provided dataset."}
  },
  "overall_score": 30,
  "overall_max": 30,
  "overall_percentage": 1,
  "verdict": "PASS",
  "pass_threshold_overall": 0.7,
  "task": "task-006-temporal-network-anomaly-detection",
  "model": "gpt-4o-mini"
}
```

The overall percentage and verdict are **recomputed defensively in C#** from the per-dimension scores — the LLM cannot game the arithmetic.

---

## 9. Repository structure

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
│       └── README.md
├── src/
│   ├── AmlAgent.Oracle/          # reference oracle (no LLM, no Docker)
│   │   ├── AmlGraph.cs           # union-find WCC + iterative Tarjan SCC
│   │   ├── OracleRunner.cs       # canonical clustering pipeline
│   │   └── Program.cs            # `aml-oracle --input ... --output ...`
│   └── AmlAgent.Harness/         # Docker orchestrator (any-language agents)
│       └── Program.cs            # `aml-harness --agent / --agent-image / --submission`
├── tests/
│   └── AmlAgent.Tests/           # xUnit
│       ├── OracleSmokeTests.cs       # in-process oracle tests
│       ├── OutputContractTests.cs    # Task 001 schema / range / sort tests
│       ├── Task006SummaryTests.cs    # Task 006 CSV + markdown tests
│       └── JudgeReportTests.cs       # judge_report.json shape + verdict
├── tasks/
│   ├── aml-transaction-network/
│   │   ├── instruction.md
│   │   ├── task.toml
│   │   └── environment/data/transfers.csv
│   └── task-006-temporal-network-anomaly-detection/
│       ├── prompt.md                  # canonical brief
│       ├── instruction.md             # alias pointer
│       ├── expected-behaviour.md      # what a good response looks like
│       ├── tests.md                   # description of the test plan
│       ├── rubric.json                # SK-as-judge scoring criteria
│       ├── task.toml
│       └── environment/data/weekly_transfers.csv
├── submissions/                       # drop external agents here (gitignored)
│   └── README.md                      # submission contract + Python example
├── docs/
│   └── research-problem.md
├── scripts/
│   └── generate_synthetic_aml_data.py # one-off Task 001 data generator
└── README.md                          # this file
```

---

## 10. Architecture

```text
                ┌────────────────────────────────────────────────────────┐
                │             aml-harness (src/AmlAgent.Harness)         │
                │                                                        │
                │   stages workspace ──► runs agent container ──► runs   │
                │   from tasks/<id>/    via Docker                two    │
                │                                                evaluators
                └─────────────┬────────────────────┬─────────────────────┘
                              │                    │
            ┌─────────────────▼────┐   ┌──────────▼──────────────────────┐
            │  Agent (Docker)      │   │  Evaluators (host)              │
            │  any language        │   │                                  │
            │                      │   │  1. AmlAgent.Tests (xUnit)       │
            │  primary:            │   │     deterministic structure      │
            │  C# + Semantic       │   │                                  │
            │  Kernel              │   │  2. aml-agent judge              │
            │                      │   │     SK chat against rubric.json  │
            │  reads /app/         │   │     → writes judge_report.json   │
            │  instruction.md      │   │                                  │
            │  writes /app/<out>   │   │  → OVERALL: PASS / FAIL          │
            └──────────────────────┘   └──────────────────────────────────┘
```

**Key design choices**

- **Agent core is Semantic Kernel.** Tools are exposed as `KernelFunction`s; the LLM drives an auto function-calling loop until it emits `DONE`. The agent never has direct access to the test code or the rubric.
- **Two evaluators in parallel.** xUnit catches structural failures the judge would over-rate; the judge catches qualitative compliance failures the xUnit tests can't express in code.
- **C# does the arithmetic.** The judge LLM produces per-dimension scores; the harness recomputes the overall percentage and verdict in C#, so the LLM cannot inflate its own pass rate.
- **The judge is grounded.** The judge prompt includes the task's underlying data so the LLM can verify cited transaction IDs really exist.
- **Workspace isolation.** Each run creates a fresh temp workspace; no two runs share state.
- **Polyglot harness.** The harness only requires a Docker image that reads `instruction.md` and writes output files — the agent language doesn't matter.

---

## 11. CLI reference

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

### `aml-harness` (the Docker orchestrator)

```cmd
dotnet run --project src\AmlAgent.Harness -- [agent-source] [--task <id>] [options]
```

| Option | Description |
|---|---|
| `--agent <name>` | Subfolder of `agents/` (default `csharp-sk`). The harness builds its `Dockerfile`. |
| `--agent-image <tag>` | Use a **pre-built** Docker image as the agent (no build step). |
| `--submission <path>` | Build the `Dockerfile` in a local folder (typically `submissions/<name>`). |
| `--task <id>` | Task folder under `tasks/`. Default: `aml-transaction-network`. |
| `--model <id>` | Override `BENCH_MODEL` passed into the agent container. |
| `--max-steps <n>` | Override `BENCH_MAX_STEPS`. |
| `--oracle` | Skip the agent container; produce output via `AmlAgent.Oracle` instead. Only valid for `aml-transaction-network`. |
| `--no-judge` | Skip the LLM-as-judge stage even if `rubric.json` exists. |
| `--keep-workspace` | Don't delete the temp workspace on exit (useful for inspection). |

### `aml-oracle` (the reference solver — no LLM, no Docker)

```cmd
dotnet run --project src\AmlAgent.Oracle -- --input <transfers.csv> --output <aml_clusters.csv>
```

---

## 12. Troubleshooting

**`dotnet --version` says something else** — install the .NET 8 SDK from <https://dotnet.microsoft.com/download/dotnet/8.0>. Newer SDKs (9.x) also work.

**`OPENAI_API_KEY is not set`** — copy `.env.example` to `.env` and put your key in. Make sure you're running commands from the repo root so the `.env` walk-up finder picks it up.

**`docker: command not found`** — install Docker Desktop. Or use `--oracle --no-judge` to verify the bench end-to-end without Docker or an LLM.

**Tests are mostly `[SKIP]`** — that's expected when you run `dotnet test` without a workspace. Workspace-dependent tests skip via `SkippableFact`. Run them through `aml-harness` (which sets `AML_BENCH_WORKSPACE`) to exercise them.

**Agent says `DONE` immediately without doing anything** — the system prompt biases toward `DONE` as a completion signal. In `chat` mode, give it a concrete instruction that asks for tool calls (e.g. "Use files.ListDir to show me what's in this folder, then summarise"). In `run` mode the instruction.md gives it real work to do, so this is rarely an issue.

**Judge returns invalid JSON** — `JudgeAgent.cs` prints the raw response and exits non-zero. Re-run; if it persists, the model is the cause — try `BENCH_JUDGE_MODEL=gpt-4o`.

**Want to see what the agent produced before tests/judge run** — pass `--keep-workspace` to the harness; it prints the workspace path and leaves it on disk.

---

## 13. Costs and safety notes

- The C# **oracle** and **xUnit tests** cost nothing. You can fully verify the bench's mechanics without an API key (`--oracle --no-judge`).
- The **judge** uses gpt-4o-mini by default. Each judge call costs roughly **$0.001–0.005** depending on how long the candidate's report is.
- A full **agent benchmark run** (Task 006, `gpt-4o-mini`, default 25 steps) typically costs **a few cents**. Increase `--max-steps` carefully.
- Your API key is read from `.env`, which is in `.gitignore`. It is never written to logs, test artefacts, or workspace files by this code.
- The bundled datasets are **synthetic**. No real customer data is present.

---

## 14. Reproducing the data

### Task 001 — `tasks/aml-transaction-network/environment/data/transfers.csv`

```cmd
python scripts\generate_synthetic_aml_data.py
```

This is the only remaining Python file in the runtime path. It is deterministic (`random.seed(42)`).

### Task 006 — `tasks/task-006-temporal-network-anomaly-detection/environment/data/weekly_transfers.csv`

Hand-crafted to be deterministic and small. Commit it directly; no generator script is required.

---

## 15. License & data disclaimer

Source code: see [LICENSE](LICENSE).

The datasets in `tasks/*/environment/data/` are **synthetic** and intended only for research and evaluation. They do not represent real customer accounts, real transfers, or real SAR cases. Any resemblance to actual financial activity is coincidental.

---

## Pulling the latest

```cmd
git pull
dotnet build AML-Agent-Bench.sln
```

If new task folders appear, they pick up automatically — the harness discovers tasks by directory name under `tasks/`. New xUnit test classes also pick up automatically when they're under `tests/AmlAgent.Tests/`.

## Getting help

- For the C# / SK agent specifics, see [agents/csharp-sk/README.md](agents/csharp-sk/README.md).
- For benching external agents, see [submissions/README.md](submissions/README.md).
- For the research framing, see [docs/research-problem.md](docs/research-problem.md).
- File an issue on the GitHub repo with the workspace path (`--keep-workspace`) and the `OVERALL` line from the harness.
