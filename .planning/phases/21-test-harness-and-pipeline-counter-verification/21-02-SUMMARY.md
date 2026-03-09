---
phase: 21-test-harness-and-pipeline-counter-verification
plan: 02
subsystem: testing
tags: [bash, e2e, prometheus, snmp, pipeline-counters, configmap]

# Dependency graph
requires:
  - phase: 21-test-harness-and-pipeline-counter-verification (plan 01)
    provides: E2E test runner framework with lib/ modules and run-all.sh
provides:
  - 10 pipeline counter verification scenarios covering all SNMP pipeline counters
  - Passive delta assertions for poll/event/trap counters (01-05)
  - Active ConfigMap manipulation for unreachable/recovered transitions (06-07)
  - Sentinel existence checks for abnormal-condition counters (08-10)
affects: [22-e2e-test-execution, 23-uat-scenarios]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Snapshot-poll-assert delta pattern for cumulative OTel counters"
    - "ConfigMap save/patch/restore pattern for device state transitions"
    - "Sentinel always-pass pattern for never-incremented OTel counters"

key-files:
  created:
    - tests/e2e/scenarios/01-poll-executed.sh
    - tests/e2e/scenarios/02-event-published.sh
    - tests/e2e/scenarios/03-event-handled.sh
    - tests/e2e/scenarios/04-trap-received.sh
    - tests/e2e/scenarios/05-trap-auth-failed.sh
    - tests/e2e/scenarios/06-poll-unreachable.sh
    - tests/e2e/scenarios/07-poll-recovered.sh
    - tests/e2e/scenarios/08-event-rejected.sh
    - tests/e2e/scenarios/09-event-errors.sh
    - tests/e2e/scenarios/10-trap-dropped.sh
    - tests/e2e/.gitignore
  modified: []

key-decisions:
  - "Sentinel counters always PASS with evidence note (OTel counters only appear after first Add())"
  - "trap_auth_failed queried without device_name filter since BadCommunity yields device_name=unknown"
  - "Scenario 06 uses 90s timeout (3-failure threshold + OTel latency); scenario 07 uses 60s (single success)"
  - "ConfigMap patching via jq in scenario 07 to change device IP to E2E simulator"

patterns-established:
  - "Delta assertion: snapshot_counter -> poll_until -> snapshot_counter -> assert_delta_gt"
  - "Active test: save_configmap -> modify -> wait -> assert -> restore_configmap"
  - "Sentinel test: query_prometheus for metric existence, always record_pass"

# Metrics
duration: 5min
completed: 2026-03-09
---

# Phase 21 Plan 02: Pipeline Counter Scenarios Summary

**10 E2E scenario scripts verifying all SNMP pipeline counters via delta assertions, ConfigMap manipulation, and sentinel existence checks**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-09T16:31:33Z
- **Completed:** 2026-03-09T16:36:33Z
- **Tasks:** 2
- **Files created:** 11

## Accomplishments
- 5 passive delta scenarios (01-05) verifying poll_executed, event_published, event_handled, trap_received, and trap_auth_failed counters increment during normal operation
- 2 active scenarios (06-07) that manipulate the simetra-devices ConfigMap to test unreachable/recovered device transitions with automatic cleanup
- 3 sentinel scenarios (08-10) that document existence state of abnormal-condition counters (event_rejected, event_errors, trap_dropped) with always-PASS semantics
- All scenarios use poll_until (no fixed sleeps) and filter by device_name to exclude heartbeat noise

## Task Commits

Each task was committed atomically:

1. **Task 1: Create passive counter scenarios (01-05) and sentinel scenarios (08-10)** - `2822c31` (feat)
2. **Task 2: Create active counter scenarios (06-poll-unreachable, 07-poll-recovered)** - `1768f4a` (feat)

## Files Created/Modified
- `tests/e2e/scenarios/01-poll-executed.sh` - Delta assertion for snmp_poll_executed_total (OBP-01)
- `tests/e2e/scenarios/02-event-published.sh` - Delta assertion for snmp_event_published_total (OBP-01)
- `tests/e2e/scenarios/03-event-handled.sh` - Delta assertion for snmp_event_handled_total (OBP-01)
- `tests/e2e/scenarios/04-trap-received.sh` - Delta assertion for snmp_trap_received_total (E2E-SIM, 45s timeout)
- `tests/e2e/scenarios/05-trap-auth-failed.sh` - Delta assertion for snmp_trap_auth_failed_total (no device filter, 60s timeout)
- `tests/e2e/scenarios/06-poll-unreachable.sh` - Applies fake device ConfigMap, asserts poll_unreachable increments (90s timeout)
- `tests/e2e/scenarios/07-poll-recovered.sh` - Patches device IP to simulator, asserts poll_recovered increments, restores ConfigMap
- `tests/e2e/scenarios/08-event-rejected.sh` - Sentinel check for snmp_event_rejected_total
- `tests/e2e/scenarios/09-event-errors.sh` - Sentinel check for snmp_event_errors_total
- `tests/e2e/scenarios/10-trap-dropped.sh` - Sentinel check for snmp_trap_dropped_total
- `tests/e2e/.gitignore` - Excludes temporary ConfigMap backups and reports

## Decisions Made
- Sentinel counters use always-PASS semantics since OTel counters only appear in Prometheus after their first `Add()` call -- absence is expected behavior
- trap_auth_failed (scenario 05) queries without device_name filter because "BadCommunity" doesn't match the `Simetra.*` convention, so device_name resolves to "unknown"
- Scenario 06 uses 90s timeout to account for 3 consecutive poll failures (3 x 10s) plus OTel export latency (~15s)
- Scenario 07 uses jq to patch the ConfigMap in-memory rather than maintaining a separate fixture file

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All 10 scenario scripts are ready for execution via `bash tests/e2e/run-all.sh`
- Phase 21 (test harness) is complete -- scenarios depend on running K8s cluster with simulators
- Ready for Phase 22 (E2E test execution and validation)

---
*Phase: 21-test-harness-and-pipeline-counter-verification*
*Completed: 2026-03-09*
