You are working inside an AML investigation container.

Use `/app/data/transfers.csv` to analyse account-to-account money movement.

Create the output file:

`/app/aml_clusters.csv`

The output must contain exactly these columns:

- `cluster_id`
- `account_count`
- `total_value`
- `circular_flow_score`
- `risk_score`

Rules:

1. Treat accounts as graph nodes.
2. Treat transfers as directed weighted edges.
3. Edge weight is the transaction amount.
4. Identify connected transaction clusters using accounts and transfers.
5. For each cluster, calculate:
   - number of unique accounts
   - total transfer value
   - circular flow score
   - composite AML risk score
6. The risk score must increase when:
   - total value is high
   - circular movement is high
   - many accounts are connected
   - SAR-linked transfers are present
   - transfers occur within short time windows
7. Normalize `risk_score` between 0 and 1.
8. Round numeric values to 4 decimal places.
9. Sort by `risk_score` descending.
10. Output only clusters with `risk_score >= 0.65`.

Important constraints:

- Do not rank individual transactions.
- Do not treat countries as graph nodes.
- Do not ignore transaction direction.
- Do not create extra columns.
- Do not change the output file path.
