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

The **reference agent**, **harness**, **reference oracle**, and **tests** are all
implemented in **C# (.NET 8)**. The primary agent (`agents/csharp-sk/`) uses
Microsoft Semantic Kernel as the agent core and is the subject of the PhD
investigation. Python is retained only for the one-off synthetic data
generator (`scripts/generate_synthetic_aml_data.py`) — the runtime path is
C# end-to-end.

The harness is intentionally agent-agnostic: any agent packaged as a Docker
image that reads `instruction.md` from `/app` can be benchmarked against the
same tasks, enabling cross-language comparison.

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
├── AML-Agent-Bench.sln           # Visual Studio 2022 solution
├── agents/
│   ├── README.md
│   └── csharp-sk/                # primary PhD agent (C# + Semantic Kernel)
│       ├── AmlAgent.csproj
│       ├── Program.cs            # subcommands: run | chat
│       ├── Agent/
│       │   ├── KernelFactory.cs
│       │   ├── BenchmarkAgent.cs # `run` mode — one-shot benchmark
│       │   └── ChatAgent.cs      # `chat` mode — interactive CMD REPL
│       ├── Tools/
│       │   ├── FileTools.cs
│       │   └── ShellTool.cs
│       ├── Dockerfile            # .NET SDK + dotnet-script sandbox
│       └── README.md
├── src/
│   ├── AmlAgent.Oracle/          # reference oracle (C# port of solve.py)
│   │   ├── AmlAgent.Oracle.csproj
│   │   ├── AmlGraph.cs           # WCC + Tarjan SCC
│   │   ├── OracleRunner.cs       # canonical clustering pipeline
│   │   └── Program.cs
│   └── AmlAgent.Harness/         # Docker orchestrator (replaces run_agent.py)
│       ├── AmlAgent.Harness.csproj
│       └── Program.cs
├── tests/
│   └── AmlAgent.Tests/           # xUnit — replaces test_outputs.py
│       ├── AmlAgent.Tests.csproj
│       ├── OracleSmokeTests.cs   # in-process oracle tests
│       └── OutputContractTests.cs# schema / range / sort on workspace
├── tasks/
│   └── aml-transaction-network/
│       ├── instruction.md
│       ├── task.toml
│       └── environment/data/transfers.csv
├── docs/research-problem.md
└── scripts/
    └── generate_synthetic_aml_data.py   # one-off data gen (Python by design)
```

## Open in Visual Studio 2022

Open `AML-Agent-Bench.sln`. Four projects load:
`AmlAgent` (the agent), `AmlAgent.Oracle`, `AmlAgent.Harness`, `AmlAgent.Tests`.

## Local CMD chat (no Docker required)

A quick way to interact with the C# agent before benchmarking:

```cmd
set OPENAI_API_KEY=sk-...
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat
```

Pre-load a task into the chat context:

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat --task aml-transaction-network
```

Inside the REPL: `/exit`, `/reset`, `/help`.

## Run the full benchmark (Docker required)

```cmd
set OPENAI_API_KEY=sk-...
dotnet run --project src\AmlAgent.Harness -- --agent csharp-sk --task aml-transaction-network
```

The harness builds the agent image, runs it in a sandboxed container against
the task's data, then runs the xUnit tests against the workspace.

## Run the reference oracle (no LLM)

To verify the test contract end-to-end without spending tokens:

```cmd
dotnet run --project src\AmlAgent.Harness -- --oracle
```

Or invoke the oracle directly:

```cmd
dotnet run --project src\AmlAgent.Oracle -- --input tasks\aml-transaction-network\environment\data\transfers.csv --output aml_clusters.csv
```

## Expected output

The agent must produce `aml_clusters.csv` at the sandbox root with exactly:

```text
cluster_id,account_count,total_value,circular_flow_score,risk_score
```

filtered to `risk_score >= 0.65`, sorted descending by `risk_score`, rounded
to 4 decimal places.

## Notes

The dataset is synthetic and is intended for research/evaluation only. It does not contain real customer or payment data.
