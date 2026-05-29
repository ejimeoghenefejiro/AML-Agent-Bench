"""
Python baseline agent for AML-Agent-Bench.

Same contract as the C# / Semantic Kernel reference agent:
  - read ${BENCH_TASK_DIR}/instruction.md (or prompt.md)
  - solve the task using available tools
  - write outputs into the workspace
  - exit 0 when done

This baseline is intentionally simple: a fixed-iteration ReAct-style loop
with three tools (list_dir, read_file, write_file) and no shell access.
It proves that:
  1. the polyglot harness path works for non-C# agents
  2. tasks and evaluators are language-neutral
  3. cross-language comparison studies are possible

Not a production agent — it doesn't execute code; it asks the LLM to author
the final outputs directly. Use it as a starting point for richer Python
submissions.
"""
from __future__ import annotations

import json
import os
import sys
import textwrap
from pathlib import Path
from typing import Any

from openai import OpenAI


SANDBOX = Path(os.environ.get("BENCH_TASK_DIR", "/app")).resolve()
MODEL = os.environ.get("BENCH_MODEL", "gpt-4o-mini")
MAX_STEPS = int(os.environ.get("BENCH_MAX_STEPS", "12"))


def find_instruction() -> Path:
    for name in ("instruction.md", "prompt.md"):
        candidate = SANDBOX / name
        if candidate.exists():
            return candidate
    raise FileNotFoundError(f"No instruction.md or prompt.md in {SANDBOX}")


def _resolve(rel_or_abs: str) -> Path:
    p = Path(rel_or_abs)
    return p if p.is_absolute() else (SANDBOX / p)


def tool_list_dir(args: dict) -> str:
    p = _resolve(args["path"])
    if not p.is_dir():
        return f"NOT_A_DIRECTORY: {p}"
    return "\n".join(str(c.name) + ("/" if c.is_dir() else "") for c in p.iterdir())


def tool_read_file(args: dict) -> str:
    p = _resolve(args["path"])
    if not p.exists():
        return f"FILE_NOT_FOUND: {p}"
    text = p.read_text(encoding="utf-8")
    limit = int(args.get("max_chars", 200_000))
    return text[:limit] + ("\n...[truncated]" if len(text) > limit else "")


def tool_write_file(args: dict) -> str:
    p = _resolve(args["path"])
    p.parent.mkdir(parents=True, exist_ok=True)
    content = args["content"]
    p.write_text(content, encoding="utf-8")
    return f"WROTE {len(content)} chars to {p}"


TOOLS_SPEC = [
    {
        "type": "function",
        "function": {
            "name": "list_dir",
            "description": "List files in a directory inside the sandbox.",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string"}},
                "required": ["path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "read_file",
            "description": "Read a UTF-8 text file from the sandbox.",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string"},
                    "max_chars": {"type": "integer"},
                },
                "required": ["path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "write_file",
            "description": "Write UTF-8 text to a file in the sandbox (overwrites).",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string"},
                    "content": {"type": "string"},
                },
                "required": ["path", "content"],
            },
        },
    },
]

DISPATCH = {
    "list_dir": tool_list_dir,
    "read_file": tool_read_file,
    "write_file": tool_write_file,
}


SYSTEM = textwrap.dedent(
    """
    You are an autonomous benchmark agent for AML-Agent-Bench, written in Python.
    You operate inside a sandboxed Linux container. Your working directory is
    the sandbox root (BENCH_TASK_DIR). You have three tools: list_dir,
    read_file, write_file. Author all required output files at the exact
    paths the task specifies. When the task is complete and you have verified
    every required output exists in the sandbox, reply with the single token
    DONE and stop calling tools.
    """
).strip()


def main() -> int:
    client = OpenAI()
    instruction = find_instruction().read_text(encoding="utf-8")

    messages: list[dict[str, Any]] = [
        {"role": "system", "content": SYSTEM},
        {
            "role": "user",
            "content": (
                f"Sandbox root: {SANDBOX}\n\n"
                "Task instructions follow. Produce the required output files.\n\n"
                "----- BEGIN INSTRUCTIONS -----\n"
                f"{instruction}\n"
                "----- END INSTRUCTIONS -----"
            ),
        },
    ]

    print(f"[python-baseline] model={MODEL} sandbox={SANDBOX} max_steps={MAX_STEPS}", flush=True)
    for step in range(1, MAX_STEPS + 1):
        print(f"--- step {step} ---", flush=True)
        resp = client.chat.completions.create(
            model=MODEL,
            messages=messages,
            tools=TOOLS_SPEC,
            tool_choice="auto",
            temperature=0.0,
        )
        msg = resp.choices[0].message
        messages.append(msg.model_dump(exclude_none=True))

        if msg.tool_calls:
            for call in msg.tool_calls:
                args = json.loads(call.function.arguments or "{}")
                try:
                    result = DISPATCH[call.function.name](args)
                except Exception as ex:
                    result = f"TOOL_ERROR: {ex}"
                print(f"[tool {call.function.name}] {result[:200]}", flush=True)
                messages.append(
                    {
                        "role": "tool",
                        "tool_call_id": call.id,
                        "content": result,
                    }
                )
            continue

        text = msg.content or ""
        print(text, flush=True)
        if "DONE" in text:
            print("Agent reported completion.", flush=True)
            return 0

    print(f"Agent exhausted {MAX_STEPS} steps without DONE.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
