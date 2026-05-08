#!/bin/bash
set -e

cd tasks/aml-transaction-network
python solution/solve.py
python -m pytest tests/test_outputs.py
rm -f aml_clusters.csv
