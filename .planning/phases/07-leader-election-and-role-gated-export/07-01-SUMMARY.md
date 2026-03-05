---
phase: 07-leader-election-and-role-gated-export
plan: 01
subsystem: telemetry
tags: [leader-election, ILeaderElection, AlwaysLeaderElection, LeaseOptions, TelemetryConstants, SiteOptions, kubernetes, configuration]

# Dependency graph
requires:
  - phase: 01-infrastructure-foundation
    provides: TelemetryConstants.MeterName, SiteOptions baseline, SiteOptionsValidator pattern
provides:
  - ILeaderElection interface (IsLeader, CurrentRole) — contract for all Phase 7 implementations
  - AlwaysLeaderElection sealed class — local dev / non-K8s fallback, always leader
  - TelemetryConstants.LeaderMeterName = "SnmpCollector.Leader" — meter discrimination key
  - LeaseOptions configuration (Name, Namespace, RenewIntervalSeconds, DurationSeconds)
  - LeaseOptionsValidator — cross-field guard (DurationSeconds > RenewIntervalSeconds)
  - SiteOptions.PodIdentity — nullable string for K8s lease holder identity
affects:
  - 07-02-K8sLeaseElection (implements ILeaderElection, consumes LeaseOptions + SiteOptions.PodIdentity)
  - 07-03-MetricRoleGatedExporter (reads ILeaderElection.IsLeader, gates on LeaderMeterName)
  - 07-04-DI-wiring (registers AlwaysLeaderElection or K8sLeaseElection, registers LeaseOptions + LeaseOptionsValidator)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ILeaderElection abstraction decouples election mechanism from consumers (MetricRoleGatedExporter, log enrichment)"
    - "AlwaysLeaderElection as sealed dev/test default — replace by DI registration, not code changes"
    - "Two-meter architecture: MeterName for all-instance pipeline metrics, LeaderMeterName for leader-only business metrics"
    - "LeaseOptionsValidator follows SiteOptionsValidator pattern: IValidateOptions<T>, failures List<string>, 'Section:Field' error format"

key-files:
  created:
    - src/SnmpCollector/Telemetry/ILeaderElection.cs
    - src/SnmpCollector/Telemetry/AlwaysLeaderElection.cs
    - src/SnmpCollector/Configuration/LeaseOptions.cs
    - src/SnmpCollector/Configuration/Validators/LeaseOptionsValidator.cs
  modified:
    - src/SnmpCollector/Telemetry/TelemetryConstants.cs
    - src/SnmpCollector/Configuration/SiteOptions.cs

key-decisions:
  - "Two-meter split: MeterName ('SnmpCollector') exported by all instances, LeaderMeterName ('SnmpCollector.Leader') exported only by leader"
  - "AlwaysLeaderElection as sealed class with expression-body properties — no state, no constructor needed"
  - "LeaseOptions default Name='snmp-collector-leader', Namespace='default' (SnmpCollector-specific, not inherited from Simetra)"
  - "SiteOptions.PodIdentity is nullable string — PostConfigure will default to HOSTNAME env var in Plan 04 DI wiring"

patterns-established:
  - "LeaseOptionsValidator pattern: IValidateOptions<LeaseOptions>, List<string> failures, 'Lease:Field' error format consistent with SiteOptionsValidator"

# Metrics
duration: 1min
completed: 2026-03-05
---

# Phase 7 Plan 01: Leader Election Foundation Types Summary

**ILeaderElection interface, AlwaysLeaderElection dev fallback, LeaseOptions config, and TelemetryConstants two-meter split establishing the contract for K8s lease election and role-gated metric export**

## Performance

- **Duration:** ~1 min
- **Started:** 2026-03-05T16:15:49Z
- **Completed:** 2026-03-05T16:16:56Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- ILeaderElection interface defined with IsLeader and CurrentRole — the contract K8sLeaseElection (Plan 02) and MetricRoleGatedExporter (Plan 03) both consume
- AlwaysLeaderElection sealed class provides safe local dev default with zero external dependencies
- TelemetryConstants split into MeterName (all-instance pipeline metrics) and LeaderMeterName (leader-only business metrics)
- LeaseOptions and LeaseOptionsValidator created following established validator pattern with cross-field DurationSeconds > RenewIntervalSeconds guard
- SiteOptions.PodIdentity added as nullable string for K8s pod name injection at lease acquisition

## Task Commits

Each task was committed atomically:

1. **Task 1: ILeaderElection interface, AlwaysLeaderElection, TelemetryConstants.LeaderMeterName** - `66bcfb2` (feat)
2. **Task 2: LeaseOptions, LeaseOptionsValidator, SiteOptions.PodIdentity** - `6fe835b` (feat)

## Files Created/Modified

- `src/SnmpCollector/Telemetry/ILeaderElection.cs` - Interface: IsLeader (bool), CurrentRole (string)
- `src/SnmpCollector/Telemetry/AlwaysLeaderElection.cs` - Sealed impl: IsLeader=true, CurrentRole="leader"
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` - Added LeaderMeterName="SnmpCollector.Leader"; updated MeterName doc
- `src/SnmpCollector/Configuration/LeaseOptions.cs` - Config: Name, Namespace, RenewIntervalSeconds (10), DurationSeconds (15)
- `src/SnmpCollector/Configuration/Validators/LeaseOptionsValidator.cs` - Cross-field: DurationSeconds > RenewIntervalSeconds
- `src/SnmpCollector/Configuration/SiteOptions.cs` - Added nullable PodIdentity property

## Decisions Made

- **Two-meter architecture:** MeterName ("SnmpCollector") exported by all instances for pipeline health; LeaderMeterName ("SnmpCollector.Leader") exported only by leader for business metrics (snmp_gauge, snmp_counter, snmp_info). This is the discrimination key MetricRoleGatedExporter (Plan 03) will use.
- **AlwaysLeaderElection as sealed:** No state, expression-body properties. Sealed prevents accidental inheritance in test code — tests that need custom behavior should mock ILeaderElection directly.
- **LeaseOptions defaults:** Name="snmp-collector-leader", Namespace="default" — SnmpCollector-specific values, not inherited from Simetra's "simetra-leader"/"simetra" defaults.
- **SiteOptions.PodIdentity is nullable:** Plan 04 DI wiring will add PostConfigure that sets it from HOSTNAME env var (K8s pod name) when null, falling back to Environment.MachineName.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

All 6 foundation types are in place. Phase 7 Plan 02 (K8sLeaseElection) can now:
- Implement ILeaderElection using Kubernetes lease API
- Consume LeaseOptions for timing parameters
- Consume SiteOptions.PodIdentity for lease holder identity

Phase 7 Plan 03 (MetricRoleGatedExporter) has:
- ILeaderElection.IsLeader to gate metric export
- TelemetryConstants.LeaderMeterName to discriminate which meter to gate

No blockers.

---
*Phase: 07-leader-election-and-role-gated-export*
*Completed: 2026-03-05*
