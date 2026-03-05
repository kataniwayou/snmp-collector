---
phase: 07-leader-election-and-role-gated-export
plan: 05
subsystem: testing
tags: [otel, metrics, leader-election, di-singleton, xunit, meter-provider, reflection]

# Dependency graph
requires:
  - phase: 07-03
    provides: MetricRoleGatedExporter implementation, SnmpMetricFactory LeaderMeterName migration
  - phase: 07-01
    provides: AlwaysLeaderElection, ILeaderElection, TelemetryConstants.LeaderMeterName
  - phase: 07-04
    provides: DI wiring: concrete-first K8sLeaseElection singleton, AlwaysLeaderElection for local dev
provides:
  - MetricRoleGatedExporterTests: 7 tests covering leader pass-through, follower filtering, all-gated Success, ParentProvider reflection, null guards
  - LeaderElectionTests: 5 tests covering AlwaysLeaderElection behavior and DI singleton pattern (SC#4, SC#5)
  - SnmpMetricFactoryTests updated to filter on LeaderMeterName (matches Plan 03 meter migration)
affects: [Phase 8, any future OTel SDK upgrade (ParentProvider breakage detection)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "MeterProvider-based integration test: real OTel SDK pipeline with PeriodicExportingMetricReader(exportIntervalMilliseconds=int.MaxValue) and ForceFlush() to control export timing"
    - "Reflection breakage detection test: Assert property exists, CanRead, CanWrite, setter is non-public — provides early warning before runtime failure on SDK upgrade"
    - "Concrete-first DI singleton test: register concrete type, forward both interface and IHostedService to same instance, assert ReferenceEquals"
    - "Anti-pattern proof test: demonstrate broken naive double-registration with Assert.NotSame"

key-files:
  created:
    - tests/SnmpCollector.Tests/Telemetry/MetricRoleGatedExporterTests.cs
    - tests/SnmpCollector.Tests/Telemetry/LeaderElectionTests.cs
  modified:
    - tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs

key-decisions:
  - "MeterProvider-based approach for gating tests: create real meters, record measurements, ForceFlush — avoids Batch<Metric> construction which requires OTel SDK internals"
  - "CapturingExporter.ExportCallCount == 0 for all-gated follower case: inner Export not called when ungated list is empty, MetricRoleGatedExporter returns Success internally"
  - "SnmpMetricFactoryTests: single-line change from MeterName to LeaderMeterName in MeterListener.InstrumentPublished filter — no other test files needed updating"
  - "LeaderElectionTests uses AlwaysLeaderElection as K8sLeaseElection stand-in: tests the DI PATTERN not K8s internals — avoids K8s cluster dependency in unit tests"

patterns-established:
  - "MeterProvider test pipeline: PeriodicExportingMetricReader(int.MaxValue) + ForceFlush() for deterministic OTel export in unit tests"
  - "Reflection breakage detection: property existence + accessibility assertions as SDK upgrade guardrail"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 7 Plan 05: Tests — MetricRoleGatedExporter, AlwaysLeaderElection, SnmpMetricFactoryTests Summary

**Unit tests validating leader/follower export gating via real OTel MeterProvider pipeline, ParentProvider reflection breakage detection, DI singleton pattern (SC#5), and SnmpMetricFactoryTests fixed for LeaderMeterName**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-05T16:33:24Z
- **Completed:** 2026-03-05T16:35:37Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments
- MetricRoleGatedExporterTests (7 tests): leader passes all metrics, follower filters LeaderMeterName, follower all-gated returns Success without calling inner exporter, ParentProvider reflection verified accessible with internal setter, null guard tests for all three constructor params
- LeaderElectionTests (5 tests): AlwaysLeaderElection behavior (SC#4), DI concrete-first singleton verified (SC#5), anti-pattern proof documents why the pattern is required
- SnmpMetricFactoryTests corrected: MeterListener filter changed from MeterName to LeaderMeterName — all 3 existing measurement tests now capture recordings correctly
- Full test suite: 114 tests, 0 failures

## Task Commits

Each task was committed atomically:

1. **Task 1: MetricRoleGatedExporter unit tests with ParentProvider breakage detection** - `332f01b` (test)
2. **Task 2: AlwaysLeaderElection tests and DI singleton verification** - `d6ca3ab` (test)
3. **Task 3: Fix SnmpMetricFactoryTests MeterListener filter for LeaderMeterName** - `60f7917` (fix)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `tests/SnmpCollector.Tests/Telemetry/MetricRoleGatedExporterTests.cs` - 7 tests for MetricRoleGatedExporter gating logic and OTel SDK breakage detection
- `tests/SnmpCollector.Tests/Telemetry/LeaderElectionTests.cs` - 5 tests for AlwaysLeaderElection and DI singleton pattern
- `tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs` - One-line MeterListener filter fix: MeterName → LeaderMeterName

## Decisions Made
- Used MeterProvider-based approach for gating tests (tests 1-3) rather than direct Batch<Metric> construction. Metric is a sealed class created by the SDK; constructing it directly is not supported. The MeterProvider approach tests the real pipeline and is more meaningful.
- `CapturingExporter.ExportCallCount == 0` assertion for the all-gated follower case: verifies the inner exporter is never invoked (ungated list is empty), which means the `ExportResult.Success` path is taken without forwarding. Tests the contract precisely.
- AlwaysLeaderElection used as K8sLeaseElection stand-in in LeaderElectionTests: the DI pattern test validates the registration technique (concrete-first forwarding), not K8s cluster behavior. Avoids requiring a live cluster for a pure DI pattern test.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 7 complete: all 5 plans done — AlwaysLeaderElection, K8sLeaseElection, MetricRoleGatedExporter, DI wiring, and test coverage
- Phase 8 can proceed: all Phase 7 success criteria verified by tests (SC#1, SC#4, SC#5)
- ParentProvider reflection breakage detection test is in place as an OTel SDK upgrade guardrail

---
*Phase: 07-leader-election-and-role-gated-export*
*Completed: 2026-03-05*
