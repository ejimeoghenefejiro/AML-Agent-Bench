import hashlib
import os
import pandas as pd

OUTPUT = "/app/aml_clusters.csv"

def test_output_exists():
    assert os.path.exists(OUTPUT), "Expected /app/aml_clusters.csv to exist"

def test_schema():
    df = pd.read_csv(OUTPUT)
    assert list(df.columns) == [
        "cluster_id",
        "account_count",
        "total_value",
        "circular_flow_score",
        "risk_score"
    ]

def test_cluster_level_output():
    df = pd.read_csv(OUTPUT)
    assert "txn_id" not in df.columns
    assert "account_id" not in df.columns
    assert "source_account" not in df.columns
    assert "destination_account" not in df.columns

def test_risk_score_rules():
    df = pd.read_csv(OUTPUT)
    assert df["risk_score"].between(0, 1).all()
    assert (df["risk_score"] >= 0.65).all()

def test_expected_cluster_count():
    df = pd.read_csv(OUTPUT)
    assert len(df) == 4

def test_sorting():
    df = pd.read_csv(OUTPUT)
    expected = df.sort_values(
        by=["risk_score", "cluster_id"],
        ascending=[False, True]
    ).reset_index(drop=True)
    pd.testing.assert_frame_equal(df.reset_index(drop=True), expected)

def test_deterministic_total_value_checksum():
    df = pd.read_csv(OUTPUT)
    total = round(float(df["total_value"].sum()), 2)
    checksum = hashlib.md5(str(total).encode()).hexdigest()
    assert checksum == "c57293760f066a7a9693166232db2356"

def test_top_cluster_is_highest_risk():
    df = pd.read_csv(OUTPUT)
    top = df.iloc[0]
    assert top["cluster_id"] == "cluster_8"
    assert round(float(top["risk_score"]), 4) == 1.0
