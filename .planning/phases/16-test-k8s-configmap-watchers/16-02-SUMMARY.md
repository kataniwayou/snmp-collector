---
phase: 16-test-k8s-configmap-watchers
plan: 02
subsystem: testing
tags: [k8s, configmap, device-watcher, hot-reload, quartz, poll-scheduler]

# Dependency graph
requires:
  - phase: 16-test-k8s-configmap-watchers
    provides: "16-01 verified OidMap watcher; cluster baseline established"
  - phase: quick-017
    provides: "Split ConfigMapWatcherService into OidMapWatcherService and DeviceWatcherService"
provides:
  - "Verified DeviceWatcherService handles all 7 device config scenarios without pod restart"
  - "Verified DynamicPollScheduler reconciliation for add/remove/reschedule"
  - "Verified graceful error handling for malformed JSON and ConfigMap deletion"
affects: [16-test-k8s-configmap-watchers]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "DeviceWatcher watch event triggers DeviceRegistry.ReloadAsync + DynamicPollScheduler.ReconcileAsync"
    - "Malformed device JSON logs ERR and retains previous config (no crash)"
    - "ConfigMap deletion logs WRN and retains current devices (no crash)"

key-files:
  created: []
  modified: []

key-decisions:
  - "OID list changes within a device do not count as reschedule in DynamicPollScheduler -- only interval changes trigger ~N rescheduled"
  - "ConfigMap restore after deletion triggers watch event and full reload"

patterns-established:
  - "Device hot-reload: add/remove devices propagates to all 3 replicas within 1 second"
  - "Interval reschedule: changing IntervalSeconds triggers ~N rescheduled in reconcile log"
  - "Defensive reload: malformed JSON and ConfigMap deletion both retain previous state"

# Metrics
duration: 5min
completed: 2026-03-08
---

# Phase 16 Plan 02: Device Watcher UAT Summary

**All 7 device hot-reload scenarios verified against 3-replica K8s cluster: add/remove/reschedule/OID-change/malformed-JSON/delete/restore**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-08T09:30:32Z
- **Completed:** 2026-03-08T09:35:53Z
- **Tasks:** 2
- **Files modified:** 0 (operational verification only)

## Accomplishments

- Verified device add (TEST-DEV-01) creates new Quartz job across all 3 replicas (+1 added, 3 total)
- Verified interval change (OBP-01 10s->30s) triggers reschedule (~1 rescheduled) with metrics continuing to flow
- Verified device removal cleans up Quartz job (-1 removed, 2 total)
- Verified OID list reduction triggers device reload (reconciliation runs, reduced OIDs take effect)
- Verified malformed JSON logs ERR and retains previous device config
- Verified ConfigMap deletion logs WRN and retains current devices
- Verified ConfigMap restore reloads cleanly back to 2 devices with ~1 rescheduled (OBP-01 interval restored)

## Scenario Results

| # | Scenario | Expected | Actual | Result |
|---|----------|----------|--------|--------|
| 1 | Add device (TEST-DEV-01) | 3 devices, +1 added | 3 devices, +1 added (all 3 pods) | PASS |
| 2 | Change interval (OBP-01 10s->30s) | ~1 rescheduled, metrics flowing | ~1 rescheduled, 25 OBP-01 series | PASS |
| 3 | Remove device (TEST-DEV-01) | 2 devices, -1 removed | 2 devices, -1 removed (all 3 pods) | PASS |
| 4 | Change OID list (reduce to L1/L2) | Reload + reconcile, L1 metrics present | Reload + reconcile, L1 confirmed | PASS |
| 5 | Malformed JSON | ERR "Failed to parse", metrics retained | ERR logged, 25 series retained | PASS |
| 6 | Delete ConfigMap | WRN "was deleted...retaining", metrics retained | WRN logged, 25 series retained | PASS |
| 7 | Restore ConfigMap | 2 devices, metrics flowing | 2 devices, ~1 rescheduled, OBP-01=25 NPB-01=64 | PASS |

**Result: 7/7 PASS, 0 issues**

## Task Commits

This plan is operational verification only -- no code files were created or modified. All work consisted of kubectl and curl commands against the live K8s cluster.

**Plan metadata:** (see docs commit below)

## Files Created/Modified

None -- operational verification only.

## Decisions Made

- OID list changes within a device trigger device reload but not scheduler reschedule (only interval changes count as reschedule). This is correct behavior -- the OID list is carried in the device config, not the Quartz job trigger.
- Used `jq` select with positive match (`select(.Name == "OBP-01" or .Name == "NPB-01")`) instead of negative match (`select(.Name != "TEST-DEV-01")`) due to bash escaping of `!` in Git Bash on Windows.

## Deviations from Plan

None -- plan executed exactly as written.

## Issues Encountered

- `jq` was not installed on the Windows host. Installed via `winget install jqlang.jq` (resolved in under 30 seconds).
- Bash `!` character in jq filter was being interpreted by shell history expansion. Worked around by using positive select instead of negative.

## User Setup Required

None -- no external service configuration required.

## Next Phase Readiness

- Device watcher fully verified. Ready for Plan 16-03 (combined OidMap + Device watcher stress test or final summary).
- ConfigMap restored to original 2-device baseline with full OID sets.
- Cluster remains healthy with all 3 replicas running.

---
*Phase: 16-test-k8s-configmap-watchers*
*Completed: 2026-03-08*
