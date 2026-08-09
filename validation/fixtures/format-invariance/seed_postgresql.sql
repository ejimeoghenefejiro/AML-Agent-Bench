-- Seeds a `transactions` table with the same logical case as transactions.csv/json/jsonl/parquet
-- in this directory, for FormatInvarianceTests's live PostgreSQL comparison (gated on
-- AML_CONN_TEST_FORMAT_INVARIANCE_PG being set -- see ConnectionProfileResolver.EnvVarNameFor).
CREATE TABLE IF NOT EXISTS transactions (
    transaction_id      TEXT PRIMARY KEY,
    source_account       TEXT NOT NULL,
    destination_account  TEXT NOT NULL,
    amount                NUMERIC(18,2) NOT NULL,
    currency               TEXT,
    timestamp               TIMESTAMPTZ NOT NULL,
    channel                 TEXT,
    jurisdiction             TEXT,
    sar_linked                BOOLEAN NOT NULL
);

TRUNCATE transactions;

INSERT INTO transactions (transaction_id, source_account, destination_account, amount, currency, timestamp, channel, jurisdiction, sar_linked) VALUES
('INV-001', 'ACC-ALPHA', 'ACC-BETA',  12345.67, 'GBP', '2026-03-15T14:30:00Z', 'wire',  'GB', true),
('INV-002', 'ACC-BETA',  'ACC-GAMMA',   999.99, 'EUR', '2026-03-16T08:15:30Z', 'ach',   'DE', false),
('INV-003', 'ACC-GAMMA', 'ACC-DELTA', 50000.00, 'USD', '2026-03-17T23:59:59Z', 'swift', 'US', true);
