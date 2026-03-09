---
phase: 20-test-simulator
plan: 02
subsystem: infra
tags: [kubernetes, snmp, e2e-testing, configmap, simulator]

# Dependency graph
requires:
  - phase: 20-test-simulator (plan 01)
    provides: E2E simulator Python source and Dockerfile
  - phase: 11-oid-map-design-and-obp-population
    provides: OID map and device ConfigMap patterns
provides:
  - K8s Deployment + Service for e2e-simulator pod
  - E2E-SIM device entry in simetra-devices ConfigMap
  - 7 mapped OID entries in simetra-oidmaps ConfigMap
  - 2 unmapped OIDs deliberately excluded for unknown-type testing
affects: [20-test-simulator plan 03+, e2e-test-suite]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Simulator deployment pattern: Deployment + ClusterIP Service + pysnmp health probes"

key-files:
  created:
    - deploy/k8s/simulators/e2e-sim-deployment.yaml
  modified:
    - deploy/k8s/snmp-collector/simetra-oidmaps.yaml
    - deploy/k8s/snmp-collector/simetra-devices.yaml

key-decisions:
  - "Poll interval 10s matching OBP-01 and NPB-01 for consistency"
  - "TRAP_INTERVAL=30s and BAD_TRAP_INTERVAL=45s for frequent test signal"

patterns-established:
  - "E2E simulator follows same deployment pattern as OBP/NPB simulators"

# Metrics
duration: 3min
completed: 2026-03-09
---

# Phase 20 Plan 02: E2E Simulator K8s Deployment Summary

**K8s Deployment + Service for e2e-simulator with 7 mapped OID entries in oidmaps and E2E-SIM device polling at 10s interval**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-09T15:47:49Z
- **Completed:** 2026-03-09T15:50:49Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Created e2e-sim-deployment.yaml with Deployment (1 replica, health probes) and ClusterIP Service
- Added 7 mapped OID entries (e2e_gauge_test through e2e_ip_test) to simetra-oidmaps ConfigMap
- Added E2E-SIM device with 7 poll OIDs at 10s interval to simetra-devices ConfigMap
- Deliberately excluded 2 unmapped OIDs (.999.2.1.0, .999.2.2.0) from oidmaps for unknown-type testing

## Task Commits

Each task was committed atomically:

1. **Task 1: Create K8s deployment manifest for e2e-simulator** - `6ab4447` (feat)
2. **Task 2: Add E2E-SIM entries to oidmaps and devices ConfigMaps** - `caaed5a` (feat)

## Files Created/Modified
- `deploy/k8s/simulators/e2e-sim-deployment.yaml` - Deployment + Service for e2e-simulator pod
- `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` - Added 7 e2e_ OID map entries
- `deploy/k8s/snmp-collector/simetra-devices.yaml` - Added E2E-SIM device with 7 poll OIDs

## Decisions Made
None - followed plan as specified.

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- E2E simulator ready to deploy alongside existing OBP and NPB simulators
- Collector ConfigMaps ready to apply for E2E-SIM polling
- E2E test suite can now be written against deterministic simulator values

---
*Phase: 20-test-simulator*
*Completed: 2026-03-09*
