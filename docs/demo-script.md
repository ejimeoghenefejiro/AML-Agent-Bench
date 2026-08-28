# Live demo script

A rehearsed demonstration of AML-Agent-Bench for the viva. Every command
below has been run and verified on the author's machine on 2026-08-07;
expected output snippets are included so a missed line is obvious in real
time.

**Important — read before the viva:** the live agent run is still
*stochastic* in principle (the agent writes its own scoring code each run),
but as of 2026-08-07 the task prompt was fixed to actually tell the agent
the `week_3 anomaly_score >= 0.7` calibration target — it was previously
only implied in a file (`expected-behaviour.md`) the agent never read.
After that fix, 8 of 9 live runs passed `OVERALL` (89%), up from roughly 1
in 4-5 before. It should PASS most of the time now, but it is still a real
LLM call — don't promise a guaranteed PASS to the examiners; if it FAILs,
treat it exactly as "the layered narrative" below describes (an expected,
explained outcome, not a bug). The EGHR and evidence-traceability numbers
(the actual primary-contribution metrics) are produced by the judge
independently of that xUnit gate and are worth leaning on regardless of
which way the gate goes — see docs/preliminary-results.md for worked
examples, including one run where the rubric scored a report 30/30 while
EGHR found 5 of 8 claims unsupported.

**Prerequisites on the demo machine:**

- .NET SDK 8 (newer is fine)
- `OPENAI_API_KEY` set in `.env` at the repo root
- (Optional) Docker Desktop running, only required for the polyglot harness demo

## Pre-flight (do this the night before AND again the morning of the viva)

```cmd
cd C:\PHD\AML-Agent-Bench\AML-Agent-Bench
dotnet build AML-Agent-Bench.sln
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s).`

If it doesn't, stop and fix — never demo a red build.

Then run the full live pipeline **2-3 times** to (a) confirm the venue's
network/API key actually works before you're in front of examiners, and
(b) bank at least one clean `OVERALL: PASS` result as insurance:

```cmd
dotnet run --project src\AmlAgent.Harness --no-build -- --task task-006 --local
```

Every run — pass or fail — is archived automatically to
`results\<timestamp>-task-006-...-csharp-sk.json`. Before you walk in, note
the filename of your best (PASS) run from that morning's pre-flight so you
can open it cold if the live call fails on the day (venue wifi, API outage,
rate limit). **Do not rely on a run from a previous day** — re-run the
morning of, so the backup reflects the current code.

## Demo — the layered narrative

Don't open with "watch this pass." Open with what's actually true: this is a
research benchmark that discriminates real agent failures, and here's the
evidence.

### 1. Show the proposal and the abstract (30 s)

Open: `C:\PHD\AML-Agent-Bench\Proposal\Oghenefejiro Ejime - PhD Research Proposal.pdf`.

Or on screen, open the README's abstract section and read the one-sentence
definition aloud: whether an autonomous AML agent's conclusions can be
reliably traced to the evidence that supports them.

### 2. "No-cost" sanity — the reference oracle (~10 s, zero risk)

```cmd
dotnet run --project src\AmlAgent.Harness --no-build -- --oracle --no-judge
```

Expected last three lines:

```text
Passed!  - Failed: 0, Passed: 10, Skipped: 11, Total: 21
[harness] --no-judge: skipping LLM judge
[harness] OVERALL: PASS (xunit=0 judge=0)
```

Talking point: *"That's the pure-C# reference oracle for Task 1 — no LLM,
100% deterministic, always passes. It proves the bench mechanics work
independent of any model."*

### 3. Live autonomous agent + judge against Task 006 (~20 s, ~$0.003)

One command runs the agent, the judge, and xUnit against the same workspace:

```cmd
dotnet run --project src\AmlAgent.Harness --no-build -- --task task-006 --local --keep-workspace
```

Say **before** you run it: *"This is a live OpenAI call. Watch the
`OVERALL` line at the end — it may say PASS or FAIL, and that's part of the
finding, not a bug: the agent authors its own scoring code each run, so its
structural accuracy varies. What doesn't vary is what the judge measures
next."*

Let it run. The console prints the workspace path — copy it, you'll need it.

### 4. Evidence traceability: the primary metric (plus the legacy EGHR check) (~15 s)

This is the headline. Point at these two lines in the console output
regardless of what `OVERALL` said:

```text
[judge] EGHR: 40.0% (2 unsupported + 0 contradicted / 5 claims)
[judge] evidence traceability: precision=33.3% recall=7.7% (matched 1/13 gold citations)
```

Talking point: *"This is the PhD's primary contribution, not the rubric.
Evidence traceability is computed with zero LLM involvement — regex citation
extraction against a hand-curated gold-evidence set of the 13 transactions
that actually substantiate this case's anomaly narrative. On this run, the
task's own rubric gave 'evidence_citation' a 3 out of 5 — sounds fine — but
the actual traceability recall shows the report cited only 1 of those 13
transactions. The rubric score and the operationalised metric are telling
different stories about the same report — exactly the discriminant-validity
question this PhD's RQ2 and RQ3 are about. EGHR is the legacy check
alongside it: the judge extracted every atomic factual claim from the report
and checked each one — any claim citing a nonexistent transaction ID is
deterministically forced to 'unsupported', so the LLM can't inflate its own
grounding."*

Open `<workspace>\judge_report.json` and scroll to the `"eghr"` and
`"evidence_traceability"` objects to show the full structured output,
including the `claims` array with each claim's citation and support label.

### 5. If `OVERALL: FAIL` happened (own it, don't dodge it)

Talking point: *"That FAIL is xUnit's `AnomalyScoreStrictlyIncreasing`
check — the agent's self-authored week_3 anomaly score came in under the
0.7 threshold this run. That's the bench catching a real numeric-reasoning
failure, which is precisely the point of a discriminating benchmark rather
than a rubber-stamp one. Here's a run from this morning's pre-flight where
it passed —"* then open the banked `results\<timestamp>-...json` from your
morning pre-flight run to show a clean PASS exists and is reproducible, just
not guaranteed every single time — which is itself the empirical finding
Phase 6 of the proposal is designed to characterise systematically across
many seeds.

### 6. Cross-model finding (no live re-run, ~30 s)

Open `docs/preliminary-results.md` on screen, scroll to "First EGHR and
evidence-traceability data point."

Talking point: *"We already have first-data evidence of this same gap
between rubric score and grounded metric, captured and version-controlled
before today."*

### 7. Wrap with the polyglot story (no live demo unless Docker is up)

Open `agents/README.md` and `submissions/README.md` on screen.

Talking point: *"The agent here is C# / Semantic Kernel, but the harness
treats any Docker image as an agent. Anyone — Python, TypeScript, Go — can
ship a Dockerfile and be benchmarked on the exact same tasks, including
against these same EGHR and traceability metrics."*

## If something goes wrong

| Symptom | Fix |
|---|---|
| `OPENAI_API_KEY is not set` | Check `.env` exists at repo root and contains the key on its own line |
| `HTTP 429 tokens: rate_limit_exceeded` | Wait 60 s; retry. Skip straight to opening the banked `results/...json` from pre-flight if you're out of time |
| No network / API down at the venue | Skip step 3 entirely; open the banked pre-flight `results/...json` and walk through steps 4-5 from that file instead of live |
| Agent says `DONE` after one step with no output | Re-run; usually transient |
| `OVERALL: FAIL` | This is expected sometimes — see step 5. Do not treat it as a failed demo |
| Tests skip everything when run standalone | `AML_BENCH_WORKSPACE` not set. `set` it to the workspace path first |
| Build fails | Run `dotnet restore` and `dotnet build` again — likely a stale NuGet cache |

## Total demo budget

| Step | Wallclock | OpenAI cost |
|---|---|---|
| 1. Proposal | 30 s | – |
| 2. Oracle | 10 s | – |
| 3. Live agent + judge + xUnit | 20 s | ~$0.003 |
| 4. EGHR / traceability walkthrough | 15 s | – |
| 5. (If needed) FAIL explanation + backup file | 20 s | – |
| 6. Cross-model / preliminary-results | 30 s | – |
| 7. Polyglot story | 30 s | – |
| **Total** | **~2.5 minutes wallclock + 3 minutes talking** | **< $0.005** |
