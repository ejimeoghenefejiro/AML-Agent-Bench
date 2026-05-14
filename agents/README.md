# Agents

Each subdirectory is a self-contained agent implementation. The harness
(`src/AmlAgent.Harness`) treats every agent as a black box that:

1. Is packaged as a Docker image built from `agents/<name>/Dockerfile`.
2. Reads its task from the mounted sandbox at `/app` (override via
   `BENCH_TASK_DIR`).
3. Reads `/app/instruction.md` and is free to write any files under `/app`.
4. Exits `0` when it believes the task is complete.

Tests run **after** the agent exits, via `dotnet test` on the host against the
same workspace. They validate output files only, so any language can be used
to implement an agent.

## Current agents

| Name        | Core                              | Status     |
|-------------|-----------------------------------|------------|
| `csharp-sk` | C# / Microsoft Semantic Kernel    | primary    |

## Adding a new-language agent for cross-language benchmarking

1. Create `agents/<your-agent>/Dockerfile`.
2. The image's `ENTRYPOINT` must run the agent loop and read
   `${BENCH_TASK_DIR:-/app}/instruction.md`.
3. The harness mounts `tasks/<task>/environment/` contents into `/app` and
   places `instruction.md` next to them — your agent does not need to know
   which task it is solving.
4. The harness passes `OPENAI_API_KEY`, `BENCH_MODEL`, `BENCH_MAX_STEPS`, and
   `BENCH_TASK_DIR` into the container.
5. Run it: `dotnet run --project src\AmlAgent.Harness -- --agent <your-agent>`.
