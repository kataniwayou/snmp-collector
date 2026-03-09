---
phase: 20-test-simulator
plan: 01
subsystem: testing
tags: [pysnmp, snmp, simulator, docker, e2e]

# Dependency graph
requires:
  - phase: 11-oid-map-design-and-obp-population
    provides: OBP simulator pattern and pysnmp structure
provides:
  - E2E test simulator with 9 static OIDs (7 mapped + 2 unmapped)
  - Dual trap loops (valid + bad-community)
  - Docker image e2e-simulator:local
affects: [20-02-PLAN (K8s deployment), 21-e2e-test-harness]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Static OID simulator for deterministic E2E assertions"
    - "Dual trap loop pattern (valid + bad-community) for negative testing"

key-files:
  created:
    - simulators/e2e-sim/e2e_simulator.py
    - simulators/e2e-sim/requirements.txt
    - simulators/e2e-sim/Dockerfile
  modified: []

key-decisions:
  - "All OID values static (no random walk) for deterministic test assertions"
  - "Trap target uses simetra-pods headless service on port 10162"
  - "Enterprise OID subtree 47477.999 with .1.x mapped and .2.x unmapped"

patterns-established:
  - "E2E simulator pattern: static values, dual trap loops, supervised tasks"

# Metrics
duration: 4min
completed: 2026-03-09
---

# Phase 20 Plan 01: E2E Test Simulator Summary

**pysnmp SNMP agent with 9 static OIDs (7 types) and dual trap loops for deterministic E2E pipeline testing**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-09T15:44:19Z
- **Completed:** 2026-03-09T15:48:00Z
- **Tasks:** 2
- **Files created:** 3

## Accomplishments
- E2E simulator serving 7 mapped OIDs covering all SNMP types (Gauge32, Integer32, Counter32, Counter64, TimeTicks, OctetString, IpAddress)
- 2 unmapped OIDs (Gauge32, OctetString) for testing unmapped OID handling
- Dual trap loops: valid community every 30s, bad community every 45s
- Docker image e2e-simulator:local builds successfully (196MB)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create e2e_simulator.py with static OIDs and dual trap loops** - `9b34667` (feat)
2. **Task 2: Create requirements.txt and Dockerfile** - `57e46eb` (chore)

## Files Created/Modified
- `simulators/e2e-sim/e2e_simulator.py` - pysnmp SNMP agent with static OIDs, DynamicInstance, dual trap loops, supervised tasks, signal handlers (276 lines)
- `simulators/e2e-sim/requirements.txt` - pysnmp==7.1.22 dependency
- `simulators/e2e-sim/Dockerfile` - python:3.12-slim Docker image

## Decisions Made
- All values static constants (no random walk) -- enables deterministic test assertions
- Trap target defaults to simetra-pods headless service (not ClusterIP) on port 10162
- Enterprise OID subtree 47477.999 with .1.x for mapped and .2.x for unmapped OIDs
- Bad community string hardcoded to "BadCommunity" (not configurable)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Simulator ready for K8s deployment (Plan 20-02)
- Static values enable exact-match assertions in E2E test harness (Phase 21)
- Docker image built and available locally

---
*Phase: 20-test-simulator*
*Completed: 2026-03-09*
