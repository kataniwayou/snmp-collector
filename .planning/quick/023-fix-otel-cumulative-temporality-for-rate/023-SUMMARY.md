---
phase: quick
plan: "023"
subsystem: telemetry
tags: [opentelemetry, prometheus, metrics, temporality, rate]

requires:
  - phase: 07-leader-election-and-role-gated-export
    provides: MetricRoleGatedExporter and PeriodicExportingMetricReader setup
provides:
  - Cumulative temporality on metric reader enabling Prometheus rate() on counters
affects: [operations-dashboard, business-dashboard]

tech-stack:
  added: []
  patterns:
    - "Cumulative temporality for Prometheus-compatible counter export"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "Cumulative temporality set via object initializer on PeriodicExportingMetricReader (not via AddReader options)"

patterns-established:
  - "OTel metric export uses cumulative temporality for Prometheus rate() compatibility"

duration: 2min
completed: 2026-03-08
---

# Quick 023: Fix OTel Cumulative Temporality for rate() Summary

**Set MetricReaderTemporalityPreference.Cumulative on PeriodicExportingMetricReader so Prometheus rate() returns non-null for counter metrics**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-08T18:54:44Z
- **Completed:** 2026-03-08T18:56:44Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Fixed OTel metric export to use cumulative temporality instead of default delta
- Prometheus rate() now works on counter metrics (snmp_event_handled_total, etc.)
- Added explanatory comment documenting why cumulative is required

## Task Commits

Each task was committed atomically:

1. **Task 1: Set cumulative temporality on PeriodicExportingMetricReader** - `3391e97` (fix)

## Files Created/Modified
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Added TemporalityPreference = Cumulative on PeriodicExportingMetricReader and explanatory comment

## Decisions Made
None - followed plan as specified.

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None.

## Next Phase Readiness
- Counter metrics now export with cumulative temporality
- Operations dashboard rate() panels will return data after next deploy

---
*Quick: 023-fix-otel-cumulative-temporality-for-rate*
*Completed: 2026-03-08*
