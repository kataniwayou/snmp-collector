---
phase: 12-npb-oid-population
plan: 01
subsystem: config
tags: [oid-map, npb, snmp, configmap]
dependency-graph:
  requires: [11-oid-map-design-and-obp-population]
  provides: [npb-oid-map-68-entries, k8s-configmap-npb-key]
  affects: [13-device-simulators]
tech-stack:
  added: []
  patterns: [oid-map-jsonc-documentation, per-port-metric-naming]
key-files:
  created:
    - src/SnmpCollector/config/oidmap-npb.json
  modified:
    - deploy/k8s/configmap.yaml
    - deploy/k8s/production/configmap.yaml
decisions:
  - id: npb-oid-tree
    decision: "NPB OIDs use 47477.100.1.{id}.0 for system, 47477.100.2.{port}.{id}.0 for per-port"
    reason: "Matches enterprise prefix pattern; separates system from per-port metrics in OID tree"
  - id: npb-metric-naming
    decision: "npb_{metric} for system, npb_port_{metric}_P{n} for per-port"
    reason: "Follows OBP naming convention (obp_{metric}_L{n}) adapted for port-based device"
metrics:
  duration: ~3 minutes
  completed: 2026-03-07
---

# Phase 12 Plan 01: NPB OID Population Summary

NPB OID map with 68 JSONC-documented entries (4 system + 64 per-port) added to config and both K8s ConfigMaps.

## What Was Done

### Task 1: Create oidmap-npb.json (0f135d9)

Created the NPB OID map file following the same structure as the OBP reference:

- Header comment block documenting device type, enterprise prefix, OID tree, and suffix maps
- 4 system metrics: cpu_util, mem_util, sys_temp, uptime (all OctetString)
- 64 per-port metrics: 8 ports x 8 metrics each (status as INTEGER, 7 counters as Counter64)
- Every entry has a JSONC comment documenting SNMP type, units/values, and expected range
- Wrapped in "OidMap" section for config binding compatibility

### Task 2: Add NPB OID map to K8s ConfigMaps (6c8199c)

Added `oidmap-npb.json` key to both dev and production ConfigMaps:

- Plain JSON (no JSONC comments) in YAML multi-line string
- All 68 entries with "OidMap" wrapper
- Placed after the existing `oidmap-obp.json` block

## Verification

- Stripped comments and parsed JSON: exactly 68 entries confirmed
- Both ConfigMaps contain the `oidmap-npb.json` key
- Auto-scan glob pattern (`oidmap-*.json`) will pick up the new file automatically

## Deviations from Plan

None -- plan executed exactly as written.

## Decisions Made

| ID | Decision | Reason |
|----|----------|--------|
| npb-oid-tree | System OIDs at .100.1.{id}.0, per-port at .100.2.{port}.{id}.0 | Clean separation in enterprise OID tree |
| npb-metric-naming | npb_{metric} / npb_port_{metric}_P{n} | Consistent with OBP convention adapted for port-based device |

## Next Phase Readiness

- NPB OID map is ready for device simulators (Phase 13) to reference
- No blockers or concerns
