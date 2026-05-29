#!/usr/bin/env bash
# AML-Agent-Bench reproducer — Linux / macOS / WSL.
# Mirrors scripts/test-bench.ps1.
#
# Usage:
#   scripts/test-bench.sh                  # local mode (no Docker)
#   scripts/test-bench.sh --mode docker
#   scripts/test-bench.sh --mode both
#   scripts/test-bench.sh --model gpt-4o --max-steps 8

set -u

MODE="local"
MODEL="gpt-4o-mini"
MAX_STEPS=12
SKIP_PYTHON=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode) MODE="$2"; shift 2;;
    --model) MODEL="$2"; shift 2;;
    --max-steps) MAX_STEPS="$2"; shift 2;;
    --skip-python) SKIP_PYTHON=1; shift;;
    -h|--help)
      sed -n '/^# Usage:/,/^$/p' "$0" | sed 's/^# //'; exit 0;;
    *) echo "unknown arg: $1" >&2; exit 64;;
  esac
done

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

declare -A RC
declare -A SECS

step() {
  local label="$1"; shift
  echo
  echo "============================================================"
  echo " $label"
  echo "============================================================"
  local start=$(date +%s)
  "$@"
  local rc=$?
  local end=$(date +%s)
  local elapsed=$((end - start))
  if [[ $rc -eq 0 ]]; then
    echo "[step] PASS (${elapsed}s)"
  else
    echo "[step] FAIL (${elapsed}s)"
  fi
  RC["$label"]=$rc
  SECS["$label"]=$elapsed
}

docker_available() {
  command -v docker >/dev/null 2>&1 && docker info --format '{{.OSType}}' >/dev/null 2>&1
}

step "1. build" \
  dotnet build "$REPO_ROOT/AML-Agent-Bench.sln" -nologo -v minimal

step "2. oracle" \
  dotnet run --project "$REPO_ROOT/src/AmlAgent.Harness" --no-build -- --oracle --no-judge

if [[ "$MODE" == "local" || "$MODE" == "both" ]]; then
  step "3a. agent (local)" \
    dotnet run --project "$REPO_ROOT/src/AmlAgent.Harness" --no-build -- \
      --agent csharp-sk \
      --task task-006-temporal-network-anomaly-detection \
      --model "$MODEL" \
      --max-steps "$MAX_STEPS" \
      --local
fi

if [[ "$MODE" == "docker" || "$MODE" == "both" ]]; then
  if docker_available; then
    step "3b. agent (docker)" \
      dotnet run --project "$REPO_ROOT/src/AmlAgent.Harness" --no-build -- \
        --agent csharp-sk \
        --task task-006-temporal-network-anomaly-detection \
        --model "$MODEL" \
        --max-steps "$MAX_STEPS"

    if [[ $SKIP_PYTHON -eq 0 ]]; then
      step "4. python-baseline" \
        dotnet run --project "$REPO_ROOT/src/AmlAgent.Harness" --no-build -- \
          --submission "$REPO_ROOT/submissions/python-baseline" \
          --task task-006-temporal-network-anomaly-detection \
          --model "$MODEL" \
          --max-steps "$MAX_STEPS"
    fi
  else
    echo "[warning] Docker not available; skipping Docker steps"
  fi
fi

echo
echo "============================================================"
echo " SUMMARY"
echo "============================================================"
any_fail=0
for label in "${!RC[@]}"; do :; done  # ensure array exists
for label in $(printf '%s\n' "${!RC[@]}" | sort); do
  rc=${RC[$label]}
  secs=${SECS[$label]}
  if [[ $rc -eq 0 ]]; then v=PASS; else v=FAIL; any_fail=1; fi
  printf '  %-22s  %s  (%ss)\n' "$label" "$v" "$secs"
done

echo
if [[ $any_fail -eq 1 ]]; then
  echo "OVERALL: FAIL  (at least one step did not return PASS)"
  exit 1
else
  echo "OVERALL: PASS  (all steps green)"
  exit 0
fi
