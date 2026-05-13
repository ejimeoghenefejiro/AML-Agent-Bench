# csharp-sk — C# + Semantic Kernel reference agent

This is the **primary agent** for AML-Agent-Bench, used as the PhD's reference
implementation. The agent core is Microsoft Semantic Kernel; tools are exposed
as `KernelFunction`s and the LLM drives a tool-calling loop until it emits
`DONE`.

The agent is language-agnostic at the *task* boundary — it only sees
`instruction.md` and a sandbox filesystem. That means the same task suite can
benchmark agents written in Python, TypeScript, Go, etc. (see `../README.md`).

## Tools exposed to the model

| Plugin    | Function   | Purpose                                                  |
|-----------|------------|----------------------------------------------------------|
| `files`   | `ListDir`  | List a directory                                         |
| `files`   | `ReadFile` | Read a UTF-8 text file                                   |
| `files`   | `WriteFile`| Author code or output files                              |
| `shell`   | `Run`      | Execute `bash -lc <cmd>` (python, pytest, ls, cat, ...)  |

## Environment variables

| Variable          | Default          | Meaning                                |
|-------------------|------------------|----------------------------------------|
| `OPENAI_API_KEY`  | (required)       | LLM credential                         |
| `BENCH_MODEL`     | `gpt-4o-mini`    | Chat completion model id               |
| `BENCH_TASK_DIR`  | `/app`           | Sandbox root (mounted by the harness)  |
| `BENCH_MAX_STEPS` | `25`             | Hard cap on agent turns                |

## Build (local, outside Docker)

```bash
dotnet build agents/csharp-sk/AmlAgent.csproj
```

## Run via the benchmark harness

```bash
python harness/run_agent.py --agent csharp-sk --task aml-transaction-network
```
