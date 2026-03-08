---
phase: 18
plan: 01
subsystem: observability
tags: [grafana, dashboard, prometheus, operations]
dependency-graph:
  requires: []
  provides: ["operations-dashboard"]
  affects: ["19-device-detail-dashboard"]
tech-stack:
  added: []
  patterns: ["grafana-importable-json", "DS_PROMETHEUS-placeholder", "service_instance_id-pod-filter"]
key-files:
  created:
    - deploy/grafana/dashboards/simetra-operations.json
  modified: []
  deleted:
    - deploy/grafana/dashboards/npb-device.json
    - deploy/grafana/dashboards/obp-device.json
    - deploy/grafana/provisioning/datasources/simetra-prometheus.yaml
decisions:
  - id: "18-01-d1"
    decision: "Stale files were untracked so deletion had no git diff; combined Task 1 and Task 2 into single commit"
    rationale: "Files from reference project were never committed, so git rm was unnecessary"
metrics:
  duration: "~3 minutes"
  completed: "2026-03-08"
---

# Phase 18 Plan 01: Operations Dashboard Summary

Grafana-importable operations dashboard with pod identity table, 11 pipeline counter panels, and 6 .NET runtime panels -- all filtered by service_instance_id pod variable.

## What Was Done

### Task 1: Delete stale reference files
- Deleted `npb-device.json`, `obp-device.json`, and `simetra-prometheus.yaml`
- Removed empty `deploy/grafana/provisioning/` directory tree
- Files were untracked (from reference project), so no git diff was generated

### Task 2: Create operations dashboard JSON
- Created `deploy/grafana/dashboards/simetra-operations.json` (1891 lines)
- Dashboard skeleton: schemaVersion 39, refresh 5s, time range now-15m, shared crosshair
- `__inputs` with DS_PROMETHEUS datasource placeholder
- Template variable `pod` populated from `label_values(snmp_event_published_total, service_instance_id)`

**Pod Identity row (y=0):**
- Table panel showing service_instance_id, k8s_pod_name, and Leader/Follower role
- Query A: count by (service_instance_id, k8s_pod_name) from snmp_event_published_total
- Query B: count by (service_instance_id) from snmp_gauge (leader-only metric)
- Merge transformation; value mappings: 1=Leader (green), null=Follower (gray)

**Pipeline Counters row (y=6):**
- 11 timeseries panels (4 per row, w=6, h=8)
- Events Published, Events Handled, Event Errors, Events Rejected
- Polls Executed, Traps Received, Trap Auth Failed, Trap Unknown Device
- Traps Dropped, Poll Unreachable, Poll Recovered
- All use `rate(METRIC{service_instance_id=~"$pod"}[$__rate_interval])`, unit: ops

**.NET Runtime row (y=31):**
- 6 timeseries panels (3 per row, w=8, h=8)
- GC Collections Rate (ops), GC Pause Time Rate (s) -- both use rate()
- Process Working Set (bytes), GC Heap Size (bytes) -- gauges, no rate()
- Thread Pool Threads (short), Thread Pool Queue Length (short) -- gauges, no rate()

## Verification Results

| Check | Result |
|-------|--------|
| Valid JSON | Pass |
| Timeseries panels | 17 (11 pipeline + 6 runtime) |
| Table panels | 1 (pod identity) |
| Row panels | 3 (Pod Identity, Pipeline Counters, .NET Runtime) |
| DS_PROMETHEUS refs | 39 |
| service_instance_id filters | 19 |
| Refresh interval | 5s |
| Stale files removed | All 3 deleted |

## Decisions Made

1. **Combined Task 1 + Task 2 into single commit** -- The stale files (npb-device.json, obp-device.json, simetra-prometheus.yaml) were untracked in git (from reference project), so deleting them produced no git diff. Combined with Task 2's dashboard creation for a single meaningful commit.

## Deviations from Plan

None -- plan executed exactly as written.

## Commits

| Hash | Message |
|------|---------|
| 1bcb198 | feat(18-01): create Simetra Operations dashboard and delete stale grafana files |

## Next Phase Readiness

- Dashboard JSON ready for manual Grafana import
- Phase 19 (Device Detail Dashboard) can proceed independently
- No blockers or concerns
