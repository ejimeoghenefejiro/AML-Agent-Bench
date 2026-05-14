# Submissions

This directory is for **agents written outside this repo** that you want to
benchmark against AML-Agent-Bench. Drop a folder here (gitignored by default,
so you don't have to commit other people's code) and point the harness at it:

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --submission submissions\my-python-agent ^
    --task task-006-temporal-network-anomaly-detection
```

## Minimum contract

A submission folder must contain a `Dockerfile`. The resulting image must:

1. Read its task from `${BENCH_TASK_DIR:-/app}/instruction.md` (or `prompt.md`).
2. Treat `/app` as the working directory; write all required output files there.
3. Exit `0` when finished. The harness ignores the exit code for grading —
   evaluation is purely on the workspace contents — but a clean exit is
   expected.
4. Honour any of these env vars the harness passes in:
   - `OPENAI_API_KEY` (or your own LLM credential)
   - `BENCH_MODEL`
   - `BENCH_MAX_STEPS`
   - `BENCH_TASK_DIR`

## Minimal Python example

```dockerfile
# submissions/python-baseline/Dockerfile
FROM python:3.11-slim
WORKDIR /app
RUN pip install --no-cache-dir openai pandas networkx
COPY agent.py /opt/agent/agent.py
ENV BENCH_TASK_DIR=/app
ENTRYPOINT ["python", "/opt/agent/agent.py"]
```

```python
# submissions/python-baseline/agent.py
import os, openai
task = open(os.path.join(os.environ.get("BENCH_TASK_DIR","/app"), "instruction.md")).read()
client = openai.OpenAI()
# ... your loop here, writing outputs into /app ...
```

## Pre-built image as a submission

If you already have a Docker image (e.g. on a private registry), you can skip
the build step and run it directly:

```cmd
dotnet run --project src\AmlAgent.Harness -- ^
    --agent-image my-registry/my-aml-agent:latest ^
    --task task-006-temporal-network-anomaly-detection
```

## What you'll get back

For every submission, the harness produces:

- a temp workspace with all files the agent wrote (use `--keep-workspace` to inspect)
- xUnit results from `AmlAgent.Tests`
- `judge_report.json` from `aml-agent judge` (if the task has a `rubric.json`)
- a single overall `PASS` / `FAIL` line at the end

The SK-based judge scores qualitative dimensions like evidence citation,
temporal reasoning, fact-vs-assumption separation, and compliance tone.
