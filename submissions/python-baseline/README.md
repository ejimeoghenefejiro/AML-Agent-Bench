# python-baseline

A minimal Python agent submission for AML-Agent-Bench, demonstrating the
polyglot harness path.

| | |
|---|---|
| Language | Python 3.11 |
| LLM provider | OpenAI (`openai` SDK 1.59) |
| Tools | `list_dir`, `read_file`, `write_file` (no shell) |
| Loop | Bounded ReAct-style, `BENCH_MAX_STEPS` |

## Run

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --submission submissions\python-baseline ^
    --task task-006-temporal-network-anomaly-detection
```

The harness builds the Dockerfile, runs the container with `/app` mounted as
the sandbox, then evaluates the workspace with the same xUnit suite and
LLM-as-judge that the C# reference agent uses.

## Why this exists

To support the cross-language comparison claim in the proposal: any agent
shipped as a Docker image that honours the AML-Agent-Bench contract can be
benchmarked on the same tasks as the C# / Semantic Kernel reference agent.

This is a baseline, not a production agent — it doesn't execute code; it
authors output files directly through `write_file`. Use it as a starting
point for richer Python submissions (with shell access, code execution, etc.).
