---
phase: quick
plan: "028"
subsystem: grafana-dashboards
tags: [grafana, dashboard, trend, delta, gauge]
dependency-graph:
  requires: [quick-026, quick-027]
  provides: [trend-colored-gauge-table]
  affects: []
tech-stack:
  added: []
  patterns: [delta-query-trend-detection, merge-transformation, value-mapping-arrows]
key-files:
  created: []
  modified:
    - deploy/grafana/dashboards/simetra-business.json
decisions:
  - id: trend-column-approach
    choice: "Separate Trend column with colored arrows instead of coloring Value cell"
    reason: "Grafana tables cannot color one column based on another column's value; a visible Trend column with its own thresholds and value mappings is the reliable approach"
metrics:
  duration: "~1 minute"
  completed: "2026-03-09"
---

# Quick 028: Gauge Trend Colored Value Cell Summary

**One-liner:** Delta-driven Trend column with colored directional arrows in the gauge metrics table using merge transformation and value mappings.

## What Changed

Added a Trend column to the gauge metrics table in the Simetra Business dashboard that provides instant visual feedback on whether gauge values are rising, falling, or stable.

### Task 1: Add delta query and trend coloring to gauge table panel
- **Commit:** cf65781
- Added Query B: `delta(snmp_gauge{...}[30s])` as instant table query
- Added `merge` transformation before `organize` to join Query A (current value) and Query B (delta) by matching labels
- Added `Value #B` field override with:
  - Display name "Trend"
  - Color-background cell options (mode: basic)
  - Fixed width of 80px
  - Thresholds: negative = dark-red, zero = text/neutral, positive = dark-green
  - Value mappings: null = "-", negative = red down-arrow, near-zero = neutral dash, positive = green up-arrow
- Updated `organize` transformation to include `Value #B` at index 6
- Info metrics table completely untouched

### Task 2: Human verification (checkpoint)
- **Status:** Pending user verification
- User should import updated dashboard and verify Trend column displays correctly

## Deviations from Plan

None - plan executed exactly as written.

## Verification Results

| Check | Result |
|-------|--------|
| JSON valid | Pass |
| Delta query exists (count=1) | Pass |
| snmp_info unchanged (count=1) | Pass |
| Merge transformation exists (count=1) | Pass |

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | cf65781 | feat(quick-028): add trend-colored delta column to gauge metrics table |
