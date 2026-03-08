---
phase: quick-026
plan: 01
subsystem: dashboards
tags: [grafana, business-dashboard, cascading-filters]
dependency-graph:
  requires: [19-01]
  provides: [refined-business-dashboard-with-cascading-filters]
  affects: []
tech-stack:
  added: []
  patterns: [cascading-template-variables]
key-files:
  created: []
  modified:
    - deploy/grafana/dashboards/simetra-business.json
decisions:
  - id: q026-d1
    title: "Use service_instance_id for Host filter"
    choice: "service_instance_id label as Host dropdown"
    reason: "Already the OTel resource attribute identifying host, consistent with operations dashboard"
metrics:
  duration: "~2 min"
  completed: 2026-03-08
---

# Quick Task 026: Business Dashboard Table and Filter Fix Summary

Cascading Host/Pod/Device filters and cleaned table columns hiding noisy telemetry SDK attributes.

## What Was Done

### Task 1: Update table column overrides and add cascading filters
**Commit:** `7f9f8b4`

**Column changes (both Gauge and Info tables):**
- Hidden: `service_instance_id`, `telemetry_sdk_language`, `telemetry_sdk_name`, `telemetry_sdk_version`
- Added: `k8s_pod_name` with displayName "Pod Name"
- Info table: Changed `snmp_type` from hidden to visible with displayName "SNMP Type"
- Updated `indexByName` ordering: k8s_pod_name(0), device_name(1), metric_name(2), oid(3), snmp_type(4), value(5)

**Cascading filter chain:**
- Host: `label_values(snmp_gauge, service_instance_id)` -- no dependency
- Pod: `label_values(snmp_gauge{service_instance_id=~"$host"}, k8s_pod_name)` -- cascades from Host
- Device: `label_values(snmp_gauge{service_instance_id=~"$host", k8s_pod_name=~"$pod"}, device_name)` -- cascades from Host + Pod

**Query updates:**
- Gauge: `snmp_gauge{service_instance_id=~"$host", k8s_pod_name=~"$pod", device_name=~"$device"}`
- Info: `snmp_info{service_instance_id=~"$host", k8s_pod_name=~"$pod", device_name=~"$device"}`

## Deviations from Plan

None - plan executed exactly as written.

## Verification

- JSON validates without errors
- 3 template variables with correct cascade chain confirmed
- Both table queries reference all 3 filter variables confirmed
