# csharp-sk — primary C# + Semantic Kernel agent

The PhD's reference agent. .NET 8 console app powered by Microsoft Semantic
Kernel. Tools are exposed as `KernelFunction`s; the LLM drives an auto
function-choice loop until it emits `DONE`.

## Commands

```text
aml-agent run                    Run the benchmark loop (reads $BENCH_TASK_DIR/instruction.md)
aml-agent chat [--task <id>]     Interactive CMD chat REPL for local testing
aml-agent help                   Usage
```

The same binary serves both inside-Docker benchmarking and outside-Docker
local testing.

## Tools

| Plugin    | Function    | Purpose                                                |
|-----------|-------------|--------------------------------------------------------|
| `files`   | `ListDir`   | List a directory                                       |
| `files`   | `ReadFile`  | Read a UTF-8 text file                                 |
| `files`   | `WriteFile` | Author code (`.csx`, `.cs`) or the task's output file  |
| `shell`   | `Run`       | Execute a shell command (cmd on Windows, bash on Linux)|

## Environment variables

| Variable          | Default          | Meaning                                       |
|-------------------|------------------|-----------------------------------------------|
| `OPENAI_API_KEY`  | (required)       | LLM credential                                |
| `BENCH_MODEL`     | `gpt-4o-mini`    | Chat completion model id                      |
| `BENCH_TASK_DIR`  | `/app` (run) / `.` (chat) | Sandbox root                       |
| `BENCH_MAX_STEPS` | `25`             | Hard cap on agent turns (run mode)            |

## Local CMD chat (no Docker required)

```cmd
set OPENAI_API_KEY=sk-...
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat
```

Or pre-load a task's instructions:

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj -- chat --task aml-transaction-network
```

Inside the chat:

```text
/exit   /quit   leave
/reset           clear history
/help            show commands
```

## Benchmark via the C# harness

```cmd
set OPENAI_API_KEY=sk-...
dotnet run --project src\AmlAgent.Harness -- --agent csharp-sk --task aml-transaction-network
```
