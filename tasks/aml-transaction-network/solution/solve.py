from pathlib import Path
import pandas as pd
import numpy as np
import networkx as nx

DATA = Path("/app/data/transfers.csv")
OUTPUT = Path("/app/aml_clusters.csv")

# Fallback for local execution from repository task directory
if not DATA.exists():
    DATA = Path("environment/data/transfers.csv")
    OUTPUT = Path("aml_clusters.csv")

df = pd.read_csv(DATA, parse_dates=["timestamp"])

# Build directed weighted transaction graph
g = nx.DiGraph()

for row in df.itertuples(index=False):
    src = row.source_account
    dst = row.destination_account
    amount = float(row.amount)

    if g.has_edge(src, dst):
        g[src][dst]["weight"] += amount
        g[src][dst]["count"] += 1
    else:
        g.add_edge(src, dst, weight=amount, count=1)

# Use weakly connected components so direction is preserved in metrics,
# but cluster membership captures account groups linked by transfers.
components = list(nx.weakly_connected_components(g))

records = []

for idx, nodes in enumerate(components, start=1):
    sub = g.subgraph(nodes).copy()

    cluster_df = df[
        df["source_account"].isin(nodes)
        & df["destination_account"].isin(nodes)
    ].copy()

    account_count = len(nodes)
    total_value = float(cluster_df["amount"].sum())
    sar_ratio = float(cluster_df["sar_linked"].mean())

    # Circular flow proxy:
    # ratio of directed edges that participate in at least one directed cycle.
    cycle_edges = set()
    for cycle in nx.simple_cycles(sub):
        if len(cycle) > 1:
            for i, src in enumerate(cycle):
                dst = cycle[(i + 1) % len(cycle)]
                cycle_edges.add((src, dst))

    edge_count = max(sub.number_of_edges(), 1)
    circular_flow_score = len(cycle_edges) / edge_count

    # Time compression:
    # suspicious clusters often move funds through many accounts quickly.
    time_span_hours = max(
        (cluster_df["timestamp"].max() - cluster_df["timestamp"].min()).total_seconds() / 3600,
        0.01,
    )
    velocity = len(cluster_df) / time_span_hours

    records.append({
        "cluster_id": f"cluster_{idx}",
        "account_count": account_count,
        "total_value": total_value,
        "circular_flow_score": circular_flow_score,
        "sar_ratio": sar_ratio,
        "velocity": velocity,
    })

out = pd.DataFrame(records)

def minmax(series):
    if series.max() == series.min():
        return series * 0
    return (series - series.min()) / (series.max() - series.min())

out["value_component"] = minmax(out["total_value"])
out["account_component"] = minmax(out["account_count"])
out["velocity_component"] = minmax(out["velocity"])
out["circular_component"] = out["circular_flow_score"].clip(0, 1)
out["sar_component"] = out["sar_ratio"].clip(0, 1)

out["risk_score"] = (
    0.35 * out["value_component"]
    + 0.25 * out["circular_component"]
    + 0.15 * out["account_component"]
    + 0.15 * out["velocity_component"]
    + 0.10 * out["sar_component"]
)

# Normalize final score for comparability
out["risk_score"] = minmax(out["risk_score"])

out = out[out["risk_score"] >= 0.65].copy()

out = out[[
    "cluster_id",
    "account_count",
    "total_value",
    "circular_flow_score",
    "risk_score"
]]

out["total_value"] = out["total_value"].round(4)
out["circular_flow_score"] = out["circular_flow_score"].round(4)
out["risk_score"] = out["risk_score"].round(4)

out = out.sort_values(
    by=["risk_score", "cluster_id"],
    ascending=[False, True]
).reset_index(drop=True)

out.to_csv(OUTPUT, index=False)
print(f"Wrote {len(out)} AML clusters to {OUTPUT}")
