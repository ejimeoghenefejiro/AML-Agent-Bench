# Reproducing the bench results

This page explains how to reproduce the test results in
[docs/preliminary-results.md](preliminary-results.md) on your own machine,
both **with Docker** and **without Docker**.

## TL;DR

```cmd
:: Without Docker (no container build, fastest)
powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1 -Mode local

:: With Docker (full polyglot path, includes Python baseline)
powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1 -Mode docker

:: Both, back-to-back
powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1 -Mode both
```

On Linux / macOS / WSL:

```bash
scripts/test-bench.sh --mode local
scripts/test-bench.sh --mode docker
scripts/test-bench.sh --mode both
```

The script runs build, the reference Oracle, the live C# / Semantic Kernel
agent (always), and (Docker only) the Python baseline submission. Each step
prints `PASS` or `FAIL` and the script ends with an overall summary.

## What each step does

| Step | What runs | Needs LLM? | Needs Docker? | Typical time | Typical cost |
|---|---|---|---|---|---|
| 1. build | `dotnet build` on the whole solution | – | – | 3 s | – |
| 2. oracle | Reference Oracle + 21 xUnit tests against a staged Task 001 workspace | – | – | 5 s | – |
| 3a. agent (local) | C# agent via `dotnet run` against Task 006, then judge + xUnit | ✅ | – | 15–60 s | ~$0.003 |
| 3b. agent (docker) | C# agent in a Docker container against Task 006, then judge + xUnit | ✅ | ✅ | 15–60 s + image build | ~$0.003 |
| 4. python-baseline | Python agent in a Docker container against Task 006, then judge + xUnit | ✅ | ✅ | 30–60 s + image build | ~$0.003 |

## Local mode (no Docker) vs Docker mode

| | Local mode (`--local`) | Docker mode (default) |
|---|---|---|
| Agent runs | as the host `dotnet` process | inside a Linux container |
| Polyglot agents supported | only the in-repo C# agent | any image (`--agent-image`) or local folder (`--submission`) |
| Reproducibility | very high | high (image build is deterministic) |
| Setup required | .NET SDK 8 + an `OPENAI_API_KEY` in `.env` | also Docker Desktop |
| First-run overhead | none | image build (a few minutes once) |

Use local mode for fast iteration on the C# agent and for environments
without Docker. Use Docker mode for cross-language testing (e.g. the Python
baseline) and to replicate the production sandbox.

## Expected output and expected variance

If everything is wired correctly you will see, for every run:

- step 1 (build) – always PASS
- step 2 (oracle) – always PASS
- step 3 (live agent) – **PASS most of the time**, occasionally FAIL
- step 4 (python baseline) – **PASS or FAIL** depending on the run

**Variance is expected** — even with `temperature = 0`, the agent's
authored code differs slightly between runs, and a single missing
`anomaly_score >= 0.7` normalisation will flip the xUnit verdict for that
run. This is itself one of the framework's findings (see "What this
preliminary data already tells us" in `preliminary-results.md`).

For the meeting demo:

- Use `--Mode local` so you don't depend on Docker daemons.
- If the agent fails, re-run once. Bench variance across two runs is small.
- The bench is correctly distinguishing good from poor agent output even
  when the run "FAILs" — that's a demonstration, not a problem.

## What "OVERALL: FAIL" actually means

| Failing step | Likely cause | Is it a problem? |
|---|---|---|
| 1. build FAIL | .NET SDK missing or wrong version | Yes — fix `dotnet --version` |
| 2. oracle FAIL | Code regression in `AmlAgent.Oracle` or tests | Yes — open an issue |
| 3. agent FAIL — `OPENAI_API_KEY not set` | `.env` not at repo root | Yes — fix `.env` |
| 3. agent FAIL — `HTTP 429 rate_limit_exceeded` | OpenAI tier-1 TPM bucket exhausted | No — wait 60 s, retry |
| 3. agent FAIL — `AnomalyScoreStrictlyIncreasing` | Agent forgot to normalise; xUnit caught it | **No — this is the bench working as designed.** It is the same kind of failure a regulator would catch |
| 3. agent FAIL — `OutputFileExists` | Agent did not produce a required output | No — agent-side defect, not bench-side |
| 4. python FAIL | Docker not running, or build context error | Yes if Docker error; no if agent error |

## Manual invocation of either mode

You don't have to use the wrapper script. The harness exposes the same
modes directly:

```cmd
:: Local mode (no Docker)
dotnet run --project src\AmlAgent.Harness -- ^
    --agent csharp-sk ^
    --task task-006-temporal-network-anomaly-detection ^
    --local

:: Docker mode (the default — no flag needed)
dotnet run --project src\AmlAgent.Harness -- ^
    --agent csharp-sk ^
    --task task-006-temporal-network-anomaly-detection

:: Polyglot — Python baseline
dotnet run --project src\AmlAgent.Harness -- ^
    --submission submissions\python-baseline ^
    --task task-006-temporal-network-anomaly-detection
```

Common flags:

| Flag | Meaning |
|---|---|
| `--model <id>` | override BENCH_MODEL (e.g. `gpt-4o-mini`, `gpt-4o`) |
| `--max-steps <n>` | cap on agent turns (default 25) |
| `--keep-workspace` | keep the staged temp workspace on exit (useful for inspection) |
| `--no-judge` | skip the LLM-as-judge stage |
| `--oracle` | use the reference Oracle instead of an agent (Task 001 only) |
