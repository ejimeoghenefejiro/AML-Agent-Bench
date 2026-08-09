# AmlAgent.ResearchValidation

Scientific/behavioural validation of AML-Agent-Bench's measurement machinery, kept
separate from `tests/AmlAgent.Tests` (which covers ordinary software correctness).
This project answers a different question: **does the benchmark measure what it
claims to measure, correctly, reproducibly, and robustly** -- not "does the code
compile and not crash".

Reads fixtures/gold data from `../../validation/` (fixtures = controlled inputs,
gold = manually-specified expected results). Never invents thresholds or claims
scientific validity beyond what a given test actually demonstrates; where the
benchmark's *implementation* disagrees with its own *stated scientific definition*
(e.g. EGHR's own doc comments), that is flagged explicitly in the test, not
silently tuned around.

See `../../validation/README.md` for the fixture/gold directory layout.
