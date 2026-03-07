#!/usr/bin/env bash
set -euo pipefail

###############################################################################
# verify-e2e.sh — End-to-end verification for Simetra SNMP pipeline
#
# Queries Prometheus to confirm polled and trap metrics arrive from both
# OBP and NPB simulators through the full pipeline:
#   simulators -> SNMP -> snmp-collector -> OTel -> Prometheus
#
# Requirements: curl, jq, kubectl
# Usage:       ./verify-e2e.sh [--prometheus-url URL]
###############################################################################

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
PROMETHEUS_URL="${PROMETHEUS_URL:-http://localhost:9090}"
POLL_INTERVAL=15          # seconds between retry attempts
TRAP_TIMEOUT=300          # 5 minutes — traps fire at random 60-300s intervals
POLL_TIMEOUT=60           # 1 minute  — polled metrics appear within ~10-20s
PORTFORWARD_PID=""
PASS_COUNT=0
FAIL_COUNT=0

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --prometheus-url)
      PROMETHEUS_URL="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1"
      echo "Usage: $0 [--prometheus-url URL]"
      exit 1
      ;;
  esac
done

# ---------------------------------------------------------------------------
# Dependency checks
# ---------------------------------------------------------------------------
for cmd in curl jq kubectl; do
  if ! command -v "$cmd" &>/dev/null; then
    echo "ERROR: '$cmd' is required but not found in PATH."
    exit 1
  fi
done

# ---------------------------------------------------------------------------
# Helper functions
# ---------------------------------------------------------------------------

cleanup() {
  if [[ -n "$PORTFORWARD_PID" ]] && kill -0 "$PORTFORWARD_PID" 2>/dev/null; then
    echo ""
    echo "Cleaning up port-forward (PID $PORTFORWARD_PID)..."
    kill "$PORTFORWARD_PID" 2>/dev/null || true
    wait "$PORTFORWARD_PID" 2>/dev/null || true
  fi
}

trap cleanup EXIT

start_port_forward() {
  echo "Starting kubectl port-forward to Prometheus..."
  kubectl port-forward -n simetra svc/prometheus 9090:9090 &>/dev/null &
  PORTFORWARD_PID=$!
  echo "  Port-forward PID: $PORTFORWARD_PID"
  echo "  Waiting 3 seconds for readiness..."
  sleep 3

  if ! kill -0 "$PORTFORWARD_PID" 2>/dev/null; then
    echo "ERROR: port-forward process died immediately."
    exit 1
  fi
  echo "  Port-forward ready."
  echo ""
}

# check_metric QUERY DESCRIPTION
#   Queries Prometheus instant query API. Returns 0 if result count > 0.
check_metric() {
  local query="$1"
  local description="$2"

  local response
  response=$(curl -s -G "${PROMETHEUS_URL}/api/v1/query" \
    --data-urlencode "query=${query}" 2>/dev/null) || true

  local status
  status=$(echo "$response" | jq -r '.status' 2>/dev/null) || true

  if [[ "$status" != "success" ]]; then
    echo "  FAIL  $description  (Prometheus query error)"
    return 1
  fi

  local count
  count=$(echo "$response" | jq '.data.result | length' 2>/dev/null) || true

  if [[ "$count" -gt 0 ]] 2>/dev/null; then
    echo "  PASS  $description  ($count series)"
    return 0
  else
    echo "  FAIL  $description  (0 series)"
    return 1
  fi
}

# wait_for_metric QUERY DESCRIPTION TIMEOUT
#   Polls check_metric until success or timeout.
wait_for_metric() {
  local query="$1"
  local description="$2"
  local timeout="$3"

  local elapsed=0

  while [[ $elapsed -lt $timeout ]]; do
    if check_metric "$query" "$description"; then
      return 0
    fi
    sleep "$POLL_INTERVAL"
    elapsed=$((elapsed + POLL_INTERVAL))
    echo "  ...waiting ($elapsed/${timeout}s) $description"
  done

  echo "  TIMEOUT  $description  (waited ${timeout}s)"
  return 1
}

record_result() {
  local result=$1
  if [[ $result -eq 0 ]]; then
    PASS_COUNT=$((PASS_COUNT + 1))
  else
    FAIL_COUNT=$((FAIL_COUNT + 1))
  fi
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

echo "============================================================"
echo " Simetra E2E Verification"
echo "============================================================"
echo ""
echo "Prometheus URL: $PROMETHEUS_URL"
echo "Poll timeout:   ${POLL_TIMEOUT}s"
echo "Trap timeout:   ${TRAP_TIMEOUT}s"
echo ""

# Start port-forward (only if using default localhost URL)
if [[ "$PROMETHEUS_URL" == "http://localhost:9090" ]]; then
  start_port_forward
fi

# -------------------------------------------------------------------
# Phase 1: Polled metrics (short timeout — should appear quickly)
# -------------------------------------------------------------------
echo "------------------------------------------------------------"
echo " Polled Metrics (timeout: ${POLL_TIMEOUT}s)"
echo "------------------------------------------------------------"
echo ""

echo "[1/6] OBP polled metrics..."
wait_for_metric \
  'snmp_gauge{device_name="OBP-01",source="poll"}' \
  "OBP-01 polled metrics exist" \
  "$POLL_TIMEOUT" && record_result 0 || record_result 1

echo ""
echo "[2/6] OBP specific metric (obp_r1_power_L1)..."
wait_for_metric \
  'snmp_gauge{metric_name="obp_r1_power_L1",device_name="OBP-01"}' \
  "OBP-01 obp_r1_power_L1 metric" \
  "$POLL_TIMEOUT" && record_result 0 || record_result 1

echo ""
echo "[3/6] NPB polled metrics..."
wait_for_metric \
  'snmp_gauge{device_name="NPB-01",source="poll"}' \
  "NPB-01 polled metrics exist" \
  "$POLL_TIMEOUT" && record_result 0 || record_result 1

echo ""
echo "[4/6] NPB specific metric (npb_cpu_util)..."
wait_for_metric \
  'snmp_gauge{metric_name="npb_cpu_util",device_name="NPB-01"}' \
  "NPB-01 npb_cpu_util metric" \
  "$POLL_TIMEOUT" && record_result 0 || record_result 1

# -------------------------------------------------------------------
# Phase 2: Trap metrics (longer timeout — traps fire at 60-300s)
# -------------------------------------------------------------------
echo ""
echo "------------------------------------------------------------"
echo " Trap Metrics (timeout: ${TRAP_TIMEOUT}s / 5 minutes)"
echo "------------------------------------------------------------"
echo ""

echo "[5/6] OBP trap metrics (obp_channel_L*)..."
wait_for_metric \
  'snmp_gauge{metric_name=~"obp_channel_L.*",source="trap",device_name="OBP-01"}' \
  "OBP-01 trap metrics (channel)" \
  "$TRAP_TIMEOUT" && record_result 0 || record_result 1

echo ""
echo "[6/6] NPB trap metrics (npb_port_status_P*)..."
wait_for_metric \
  'snmp_gauge{metric_name=~"npb_port_status_P.*",source="trap",device_name="NPB-01"}' \
  "NPB-01 trap metrics (port status)" \
  "$TRAP_TIMEOUT" && record_result 0 || record_result 1

# -------------------------------------------------------------------
# Summary
# -------------------------------------------------------------------
TOTAL=$((PASS_COUNT + FAIL_COUNT))

echo ""
echo "============================================================"
echo " Summary"
echo "============================================================"
echo ""
echo "  Passed: $PASS_COUNT / $TOTAL"
echo "  Failed: $FAIL_COUNT / $TOTAL"
echo ""

if [[ $FAIL_COUNT -gt 0 ]]; then
  echo "RESULT: FAIL"
  exit 1
else
  echo "RESULT: PASS"
  exit 0
fi
