# Investigator Field Notes — Case task-007-mule-network

These are informal notes from the intake analyst. They are **not** verified evidence — treat them as context only, and do not cite them as if they were transaction data. Anything you assert as fact must be traceable to `data/` or `case_manifest.json`.

- 2026-02-02: Customer N100 (elderly, first fraud report with this bank) called in distressed, saying she had been asked by "her bank's fraud team" over the phone to move funds to a "safe account" to protect them. Classic authorised-push-payment (APP) social-engineering pattern.
- M201 and M202 were both opened within the last week, per onboarding records (not attached here). New accounts receiving a large first-time inbound transfer from an elderly customer is a known typology red flag.
- M301 has a prior SAR filed by a different institution (see `relationships.graphml`, `WATCHLIST1` node) — worth corroborating independently rather than taking on faith.
- N150 is a long-standing customer (8 years) who pays a small monthly amount to M201 — analyst's working assumption is this is an unrelated rent or subscription payment that predates M201's compromise, but this has **not** been confirmed with N150 directly.
- N160/N170 showed up in the correspondent bank's export only because they process payments through the same clearing batch as M301's transactions that day — no known relationship to this case, but flagging in case a link turns up later.
- Note for the report: the archive system and the correspondent bank's live feed disagree on the settlement date for the exit transfer to EXT401 — check `case_manifest.json` before writing that up, don't just pick whichever number arrives first.
