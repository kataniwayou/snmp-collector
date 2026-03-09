---
phase: 21-test-harness-and-pipeline-counter-verification
plan: 01
subsystem: testing
tags: [bash, e2e, prometheus, kubectl, test-runner]

requires:
  - phase: 20-test-simulator
    provides: E2E-SIM device and simulator deployment for test scenarios
provides:
  - E2E test runner framework (run-all.sh entry point)
  - Reusable Prometheus query/poll/snapshot utilities
  - Port-forward lifecycle management with trap cleanup
  - Pass/fail tracking with markdown report generation
  - FAKE-UNREACHABLE device fixture for unreachability testing
affects: [21-02, 22-trap-counter-verification, 23-oid-resolution-verification, 24-edge-case-verification]

tech-stack:
  added: []
  patterns: [poll-until-satisfied with 30s timeout/3s interval, delta-based counter assertions, sequential scenario execution]

key-files:
  created:
    - tests/e2e/run-all.sh
    - tests/e2e/lib/common.sh
    - tests/e2e/lib/prometheus.sh
    - tests/e2e/lib/kubectl.sh
    - tests/e2e/lib/report.sh
    - tests/e2e/fixtures/fake-device-configmap.yaml
  modified: []

key-decisions:
  - "POSIX-compatible function definitions with bash set -euo pipefail for strict error handling"
  - "poll_until uses date +%s for deadline (not $SECONDS) to avoid subshell issues"
  - "query_counter wraps PromQL with 'or vector(0)' to handle missing counters gracefully"

patterns-established:
  - "Delta assertion pattern: snapshot_counter before, wait for activity, snapshot after, assert_delta_gt"
  - "Scenario scripts sourced sequentially from scenarios/[0-9]*.sh -- each file is self-contained"
  - "Port-forward lifecycle: start in runner, trap EXIT cleanup, shared across all scenarios"

duration: 8min
completed: 2026-03-09
---

# Phase 21 Plan 01: E2E Test Runner Framework Summary

**Bash E2E test runner with Prometheus poll-until-satisfied utilities, delta assertions, port-forward lifecycle, and markdown report generation**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-09T16:26:00Z
- **Completed:** 2026-03-09T16:34:00Z
- **Tasks:** 2
- **Files created:** 7 (including .gitkeep)

## Accomplishments
- Created modular test runner framework with 4 library modules in lib/
- Built Prometheus query utilities with poll-until-satisfied pattern (30s timeout, 3s interval)
- Implemented delta-based counter assertions and metric existence checks
- Created FAKE-UNREACHABLE device fixture preserving all 3 existing devices (OBP-01, NPB-01, E2E-SIM)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create lib/ utility modules** - `a006826` (feat)
2. **Task 2: Create run-all.sh and fake-device fixture** - `43baa22` (feat)

## Files Created/Modified
- `tests/e2e/run-all.sh` - Entry point: pre-flight, port-forwards, scenario execution, reporting
- `tests/e2e/lib/common.sh` - Logging, pass/fail tracking, delta/exists assertions
- `tests/e2e/lib/prometheus.sh` - Prometheus HTTP API query, poll, snapshot utilities
- `tests/e2e/lib/kubectl.sh` - Port-forward lifecycle, pod readiness, ConfigMap management
- `tests/e2e/lib/report.sh` - Markdown report generation with results table and evidence
- `tests/e2e/fixtures/fake-device-configmap.yaml` - Extended ConfigMap with FAKE-UNREACHABLE device
- `tests/e2e/scenarios/.gitkeep` - Empty scenarios directory ready for Plan 02

## Decisions Made
- Used `date +%s` for deadline calculation in poll_until (not `$SECONDS`) to avoid subshell variable scope issues
- Wrapped PromQL counters with `or vector(0)` so query_counter returns 0 for not-yet-created counters instead of erroring
- assert_exists uses `{__name__="metric"}` query to find any label combination of a metric

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Framework complete and ready for scenario scripts in Plan 02
- All lib/ functions callable from sourced scenario scripts
- FAKE-UNREACHABLE fixture ready for unreachability/recovery testing

---
*Phase: 21-test-harness-and-pipeline-counter-verification*
*Completed: 2026-03-09*
