-- Seeds a `transactions` table with the same logical case as transactions.csv/json/jsonl/parquet
-- in this directory, for FormatInvarianceTests's live SQL Server comparison (gated on
-- AML_CONN_TEST_FORMAT_INVARIANCE_MSSQL being set -- see ConnectionProfileResolver.EnvVarNameFor).
IF OBJECT_ID('dbo.transactions', 'U') IS NULL
CREATE TABLE dbo.transactions (
    transaction_id       NVARCHAR(64) PRIMARY KEY,
    source_account        NVARCHAR(64) NOT NULL,
    destination_account   NVARCHAR(64) NOT NULL,
    amount                  DECIMAL(18,2) NOT NULL,
    currency                 NVARCHAR(8),
    [timestamp]               DATETIMEOFFSET NOT NULL,
    channel                   NVARCHAR(32),
    jurisdiction               NVARCHAR(8),
    sar_linked                  BIT NOT NULL
);

TRUNCATE TABLE dbo.transactions;

INSERT INTO dbo.transactions (transaction_id, source_account, destination_account, amount, currency, [timestamp], channel, jurisdiction, sar_linked) VALUES
('INV-001', 'ACC-ALPHA', 'ACC-BETA',  12345.67, 'GBP', '2026-03-15T14:30:00Z', 'wire',  'GB', 1),
('INV-002', 'ACC-BETA',  'ACC-GAMMA',   999.99, 'EUR', '2026-03-16T08:15:30Z', 'ach',   'DE', 0),
('INV-003', 'ACC-GAMMA', 'ACC-DELTA', 50000.00, 'USD', '2026-03-17T23:59:59Z', 'swift', 'US', 1);
