"""Generic Docker-based runner for AML-Agent-Bench.

Builds an agent image, runs the agent against a task in an isolated container,
then runs the task's tests in a fresh container against the same workspace.

Example:
    python harness/run_agent.py --agent csharp-sk --task aml-transaction-network

The agent and task are decoupled — any language's agent can be benchmarked as
long as it is packaged per agents/README.md.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


def sh(cmd: list[str], **kw) -> int:
    print("$", " ".join(cmd), flush=True)
    return subprocess.call(cmd, **kw)


def build_agent_image(agent: str) -> str:
    tag = f"aml-bench-agent-{agent}:latest"
    ctx = REPO / "agents" / agent
    if not (ctx / "Dockerfile").exists():
        sys.exit(f"agent not found: {ctx}")
    if sh(["docker", "build", "-t", tag, str(ctx)]) != 0:
        sys.exit("agent image build failed")
    return tag


def build_task_image(task: str) -> str:
    tag = f"aml-bench-task-{task}:latest"
    ctx = REPO / "tasks" / task / "environment"
    if not (ctx / "Dockerfile").exists():
        sys.exit(f"task environment not found: {ctx}")
    if sh(["docker", "build", "-t", tag, str(ctx)]) != 0:
        sys.exit("task image build failed")
    return tag


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--agent", required=True, help="agent name under agents/")
    ap.add_argument("--task", required=True, help="task name under tasks/")
    ap.add_argument("--model", default=os.environ.get("BENCH_MODEL", "gpt-4o-mini"))
    ap.add_argument("--max-steps", type=int, default=25)
    ap.add_argument("--keep-workspace", action="store_true")
    args = ap.parse_args()

    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        sys.exit("OPENAI_API_KEY not set in environment")

    task_dir = REPO / "tasks" / args.task
    if not task_dir.exists():
        sys.exit(f"task not found: {task_dir}")

    agent_tag = build_agent_image(args.agent)
    # Test image reuses the task's environment definition so tests run with the
    # same dependencies the task author specified.
    task_tag = build_task_image(args.task)

    workspace = Path(tempfile.mkdtemp(prefix=f"aml-bench-{args.task}-"))
    try:
        # Seed workspace from environment/ (data) + instruction.md
        env_src = task_dir / "environment"
        for item in env_src.iterdir():
            if item.name == "Dockerfile":
                continue
            dst = workspace / item.name
            if item.is_dir():
                shutil.copytree(item, dst)
            else:
                shutil.copy2(item, dst)
        shutil.copy2(task_dir / "instruction.md", workspace / "instruction.md")

        ws_mount = str(workspace).replace("\\", "/")

        print(f"\n=== running agent {args.agent} on task {args.task} ===")
        agent_rc = sh([
            "docker", "run", "--rm",
            "-v", f"{ws_mount}:/app",
            "-e", f"OPENAI_API_KEY={api_key}",
            "-e", f"BENCH_MODEL={args.model}",
            "-e", f"BENCH_MAX_STEPS={args.max_steps}",
            "-e", "BENCH_TASK_DIR=/app",
            agent_tag,
        ])
        print(f"agent exit code: {agent_rc}")

        print(f"\n=== running tests for task {args.task} ===")
        tests_src = str((task_dir / "tests").as_posix())
        test_rc = sh([
            "docker", "run", "--rm",
            "-v", f"{ws_mount}:/app",
            "-v", f"{tests_src}:/tests:ro",
            "--workdir", "/app",
            task_tag,
            "python", "-m", "pytest", "/tests/test_outputs.py", "-q",
        ])
        print(f"\ntest exit code: {test_rc}")
        return test_rc
    finally:
        if args.keep_workspace:
            print(f"workspace kept at: {workspace}")
        else:
            shutil.rmtree(workspace, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
