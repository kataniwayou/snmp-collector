#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# prometheus.sh -- Prometheus HTTP API utilities for E2E tests
# ============================================================================

# Constants
PROM_URL="http://localhost:9090"
POLL_TIMEOUT=30
POLL_INTERVAL=3

# ---------------------------------------------------------------------------
# Core query
# ---------------------------------------------------------------------------

query_prometheus() {
    local promql="$1"
    local response
    response=$(curl -sf -G "${PROM_URL}/api/v1/query" --data-urlencode "query=${promql}" 2>&1) || {
        log_error "Prometheus query failed: ${promql}"
        log_error "Response: ${response}"
        return 1
    }
    echo "$response"
}

# ---------------------------------------------------------------------------
# Counter queries
# ---------------------------------------------------------------------------

query_counter() {
    local metric_name="$1"
    local label_filter="${2:-}"

    local promql
    if [ -n "$label_filter" ]; then
        promql="sum(${metric_name}{${label_filter}}) or vector(0)"
    else
        promql="sum(${metric_name}) or vector(0)"
    fi

    local response
    response=$(query_prometheus "$promql")
    echo "$response" | jq -r '.data.result[0].value[1] // "0"' | cut -d. -f1
}

snapshot_counter() {
    query_counter "$@"
}

# ---------------------------------------------------------------------------
# Polling utilities
# ---------------------------------------------------------------------------

poll_until() {
    local timeout="$1"
    local interval="$2"
    local metric_name="$3"
    local label_filter="$4"
    local baseline="$5"

    local deadline
    deadline=$(( $(date +%s) + timeout ))

    while [ "$(date +%s)" -lt "$deadline" ]; do
        local current
        current=$(query_counter "$metric_name" "$label_filter")
        if [ "$current" -gt "$baseline" ]; then
            return 0
        fi
        sleep "$interval"
    done

    return 1
}

poll_until_exists() {
    local timeout="$1"
    local interval="$2"
    local metric_name="$3"

    local deadline
    deadline=$(( $(date +%s) + timeout ))

    while [ "$(date +%s)" -lt "$deadline" ]; do
        local result
        result=$(query_prometheus "{__name__=\"${metric_name}\"}")
        local count
        count=$(echo "$result" | jq -r '.data.result | length')
        if [ "$count" -gt 0 ]; then
            return 0
        fi
        sleep "$interval"
    done

    return 1
}

# ---------------------------------------------------------------------------
# Evidence formatting
# ---------------------------------------------------------------------------

get_evidence() {
    local metric_name="$1"
    local label_filter="${2:-}"

    local current
    current=$(query_counter "$metric_name" "$label_filter")

    local query
    if [ -n "$label_filter" ]; then
        query="sum(${metric_name}{${label_filter}})"
    else
        query="sum(${metric_name})"
    fi

    echo "metric=${metric_name} query='${query}' value=${current}"
}
