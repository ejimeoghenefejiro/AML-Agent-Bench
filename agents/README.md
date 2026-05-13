# Agents

Each subdirectory is a self-contained agent implementation. The harness
(`../harness/run_agent.py`) treats every agent as a black box that:

1. Is packaged as a Docker image built from `agents/<name>/Dockerfile`.
2. Reads its task from the mounted sandbox at `/app` (configurable via
   `BENCH_TASK_DIR`).
3. Reads `/app/instruction.md` and is free to write any files under `/app`.
4. Exits `0` when it believes the task is complete.

Tests run **after** the agent exits, in a fresh container, against `/app`.
Tests are written in Python (`tasks/*/tests/`) but only validate output files,
so the agent itself may be written in any language.

## Current agents

| Name        | Core                              | Status     |
|-------------|-----------------------------------|------------|
| `csharp-sk` | C# / Microsoft Semantic Kernel    | primary    |

## Adding a new agent (e.g. for cross-language benchmarking)

1. Create `agents/<your-agent>/Dockerfile`.
2. The image's `ENTRYPOINT` must run the agent loop and read
   `${BENCH_TASK_DIR:-/app}/instruction.md`.
3. The harness mounts `tasks/<task-id>/environment/` contents into `/app` and
   places `instruction.md` next to them — your agent does not need to know
   which task it is solving.
4. Pass `OPENAI_API_KEY` (or any LLM credential of your choice) through the
   harness env.
