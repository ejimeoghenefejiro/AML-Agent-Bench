# harness

Generic, language-agnostic runner. Builds two Docker images per run:

- the **agent image** from `agents/<agent>/Dockerfile`
- the **task image** from `tasks/<task>/environment/Dockerfile` (reused for tests)

Then it:

1. Stages a temp workspace with the task's `environment/` contents plus
   `instruction.md`.
2. Mounts that workspace into the agent container at `/app`.
3. After the agent exits, mounts the same workspace into a fresh task container
   and runs `tests/test_outputs.py`.

The agent never sees the tests, and the tests never see the agent — only the
output files in the shared workspace. That isolation is what makes the bench
fair across agent languages.

## Usage

```bash
export OPENAI_API_KEY=sk-...
python harness/run_agent.py --agent csharp-sk --task aml-transaction-network
```

Options:

- `--model`       LLM id (default `gpt-4o-mini`)
- `--max-steps`   Max agent turns (default 25)
- `--keep-workspace`  Don't delete the staging dir on exit (useful for debugging)
