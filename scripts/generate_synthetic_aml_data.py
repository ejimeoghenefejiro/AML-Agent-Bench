import csv
import random
from pathlib import Path
from datetime import datetime, timedelta

random.seed(42)

out = Path(__file__).resolve().parents[1] / "tasks" / "aml-transaction-network" / "environment" / "data" / "transfers.csv"
out.parent.mkdir(parents=True, exist_ok=True)

rows = []
base = datetime(2026, 1, 1, 9, 0, 0)

# Suspicious circular clusters
clusters = [
    ("C001", ["A1001", "A1002", "A1003", "A1004"], 18500),
    ("C002", ["A2001", "A2002", "A2003", "A2004", "A2005"], 22500),
    ("C003", ["A3001", "A3002", "A3003"], 27500),
    ("C004", ["A4001", "A4002", "A4003", "A4004"], 31000),
    ("C005", ["A5001", "A5002", "A5003", "A5004", "A5005"], 42000),
    ("C006", ["A6001", "A6002", "A6003"], 52000),
    ("C007", ["A7001", "A7002", "A7003", "A7004"], 61000),
    ("C008", ["A8001", "A8002", "A8003", "A8004", "A8005"], 73500),
]

txn_no = 1

for idx, (cluster_id, accounts, base_amount) in enumerate(clusters):
    start = base + timedelta(days=idx)
    # circular ring
    for i, src in enumerate(accounts):
        dst = accounts[(i + 1) % len(accounts)]
        amount = base_amount + random.randint(-1500, 1500)
        rows.append({
            "txn_id": f"T{txn_no:05d}",
            "timestamp": (start + timedelta(minutes=i * 7)).isoformat(),
            "source_account": src,
            "destination_account": dst,
            "amount": round(amount, 2),
            "source_country": "GB",
            "destination_country": random.choice(["AE", "NG", "TR", "CY"]),
            "sar_linked": 1 if idx in [3, 4, 5, 6, 7] else 0,
        })
        txn_no += 1

    # short-window layering transfers inside cluster
    for j in range(len(accounts) * 2):
        src, dst = random.sample(accounts, 2)
        amount = base_amount * random.uniform(0.55, 1.25)
        rows.append({
            "txn_id": f"T{txn_no:05d}",
            "timestamp": (start + timedelta(minutes=40 + j * 3)).isoformat(),
            "source_account": src,
            "destination_account": dst,
            "amount": round(amount, 2),
            "source_country": random.choice(["GB", "AE", "NG", "TR"]),
            "destination_country": random.choice(["GB", "AE", "NG", "TR", "CY"]),
            "sar_linked": 1 if idx in [4, 5, 6, 7] and j % 2 == 0 else 0,
        })
        txn_no += 1

# Benign low-risk transfers, mostly chains without circularity
for group in range(18):
    accounts = [f"B{group:02d}{i:02d}" for i in range(1, 5)]
    start = base + timedelta(days=20 + group)
    for i in range(3):
        rows.append({
            "txn_id": f"T{txn_no:05d}",
            "timestamp": (start + timedelta(hours=i * 8)).isoformat(),
            "source_account": accounts[i],
            "destination_account": accounts[i + 1],
            "amount": round(random.uniform(250, 3500), 2),
            "source_country": "GB",
            "destination_country": "GB",
            "sar_linked": 0,
        })
        txn_no += 1

with out.open("w", newline="", encoding="utf-8") as f:
    writer = csv.DictWriter(f, fieldnames=[
        "txn_id", "timestamp", "source_account", "destination_account", "amount",
        "source_country", "destination_country", "sar_linked"
    ])
    writer.writeheader()
    writer.writerows(rows)

print(f"Wrote {len(rows)} rows to {out}")
