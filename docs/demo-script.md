# Live demo script

A rehearsed five-minute demonstration of AML-Agent-Bench for a supervisor or
external reviewer. Every command below has been verified on the author's
machine; expected output snippets are included so a missed line is obvious in
real time.

**Prerequisites on the demo machine:**

- .NET SDK 8 (newer is fine)
- `OPENAI_API_KEY` set in `.env` at the repo root
- (Optional) Docker Desktop running, only required for the polyglot harness demo

## Pre-flight (do this once before the meeting starts)

```cmd
cd C:\PHD\AML-Agent-Bench\AML-Agent-Bench
dotnet build AML-Agent-Bench.sln
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s).`

If it doesn't, stop and fix — never demo a red build.

## Demo

### 1. Show the proposal and the abstract (30 s)

Open in Word: `C:\PHD\AML-Agent-Bench\Proposal\PhD-Proposal-AML-Agent-Bench.docx`.

Or on screen, open the README's abstract section in any markdown viewer and
read the three research questions aloud.

### 2. "No-cost" sanity — the reference oracle (~10 s)

```cmd
dotnet run --project src\AmlAgent.Harness --no-build -- --oracle --no-judge
```

Expected last three lines:

```text
Passed!  - Failed: 0, Passed: 10, Skipped: 11, Total: 21
[harness] --no-judge: skipping LLM judge
[harness] OVERALL: PASS (xunit=0 judge=0)
```

Talking point: *"That ran the pure-C# reference oracle for Task 1 and validated
it with 10 structural tests, in about 10 seconds, with no LLM call."*

### 3. Live autonomous agent against Task 006 (~15 s, ~$0.002)

```cmd
:: stage workspace
powershell -Command "$ws = Join-Path $env:TEMP ('demo-' + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $ws -Force | Out-Null; Copy-Item -Recurse tasks\task-006-temporal-network-anomaly-detection\environment\data (Join-Path $ws data); Copy-Item tasks\task-006-temporal-network-anomaly-detection\prompt.md $ws; Copy-Item tasks\task-006-temporal-network-anomaly-detection\instruction.md $ws; $env:BENCH_TASK_DIR=$ws; $env:BENCH_MODEL='gpt-4o-mini'; $env:BENCH_MAX_STEPS='12'; dotnet run --project agents\csharp-sk\AmlAgent.csproj --no-build -- run; Write-Output ('WORKSPACE=' + $ws)"
```

Talking point: *"That's a real OpenAI call. The agent read the prompt, authored
code, executed it, produced the two required outputs, and reported DONE."*

Show the produced files:

```cmd
type %TEMP%\demo-<...>\temporal_anomaly_summary.csv
type %TEMP%\demo-<...>\temporal_anomaly_report.md
```

### 4. Score the agent — LLM-as-judge (~5 s, ~$0.001)

```cmd
dotnet run --project agents\csharp-sk\AmlAgent.csproj --no-build -- ^
    judge --task task-006-temporal-network-anomaly-detection ^
    --workspace %TEMP%\demo-<...>
```

Expected last three lines:

```text
[judge] overall: 29/30 = 96.7%
[judge] verdict: PASS (threshold 70%)
```

Open `%TEMP%\demo-<...>\judge_report.json` and show the six per-dimension scores.

Talking point: *"That's the C# Semantic Kernel core acting as the judge. It
loaded the rubric, the candidate's markdown report, and the ground-truth
data, and produced structured JSON scoring six regulatory dimensions. The
verdict is recomputed in C# so the LLM can't inflate its own score."*

### 5. Show that xUnit and the judge are complementary (~3 s)

```cmd
set AML_BENCH_WORKSPACE=%TEMP%\demo-<...>
dotnet test tests\AmlAgent.Tests\AmlAgent.Tests.csproj --no-build --nologo -v minimal
```

Expected: `Passed! - Failed: 0, Passed: 15, Skipped: 6, Total: 21`.

Talking point: *"Two independent evaluators against the same workspace —
deterministic structural correctness and LLM-graded qualitative compliance.
Either failing produces an overall FAIL."*

### 6. Cross-model finding (no live re-run) (~30 s)

Open `docs/preliminary-results.md` on screen.

Talking point: *"And we already have a first cross-model finding. gpt-4o-mini
passed both evaluators at 96.7%. gpt-4o was more accurate on the counts but
forgot to normalise the anomaly score and was rate-limited before fixing it,
so it failed the xUnit `AnomalyScoresInRange` assertion. The bench
discriminates between models in a non-trivial way — a stronger model on a
tighter rate-limit produced worse-on-test output. That's the kind of
finding the PhD is set up to characterise systematically."*

### 7. Wrap with the polyglot story (no live demo unless Docker is up)

Open `agents/README.md` and `submissions/README.md` on screen.

Talking point: *"The agent here is C# / Semantic Kernel, but the harness
treats any Docker image as an agent. Anyone — Python, TypeScript, Go — can
ship a Dockerfile and be benchmarked on the exact same tasks. That's how we
get cross-language comparison."*

## If something goes wrong

| Symptom | Fix |
|---|---|
| `OPENAI_API_KEY is not set` | Check `.env` exists at repo root and contains the key on its own line |
| `HTTP 429 tokens: rate_limit_exceeded` | Wait 60 s; retry with smaller `BENCH_MAX_STEPS`. Skip the live agent step if it persists — `--oracle --no-judge` still runs |
| Agent says `DONE` after one step with no output | Re-run; usually transient. If persistent, the model is mis-reading the prompt — try `--model gpt-4o` |
| Tests skip everything | `AML_BENCH_WORKSPACE` not set. `set` it to the workspace path |
| Build fails | Run `dotnet restore` and `dotnet build` again — likely a stale NuGet cache |

## Total demo budget

| Step | Wallclock | OpenAI cost |
|---|---|---|
| 1. Proposal | 30 s | – |
| 2. Oracle | 10 s | – |
| 3. Live agent | 15 s | ~$0.002 |
| 4. Judge | 5 s | ~$0.001 |
| 5. xUnit | 3 s | – |
| 6. Cross-model | 30 s | – |
| 7. Polyglot story | 30 s | – |
| **Total** | **~2 minutes wallclock + 3 minutes talking** | **< $0.005** |
