---
phase: 07-leader-election-and-role-gated-export
plan: 02
subsystem: infra
tags: [kubernetes, leader-election, lease, background-service, k8s, KubernetesClient, ha]

# Dependency graph
requires:
  - phase: 07-01
    provides: ILeaderElection interface, LeaseOptions, SiteOptions.PodIdentity foundation types

provides:
  - K8sLeaseElection BackgroundService with Kubernetes Lease-based leader election
  - KubernetesClient 18.0.13 NuGet package in SnmpCollector.csproj
  - Volatile IsLeader flag updated exclusively by LeaderElector event handlers
  - StopAsync deletes lease on graceful shutdown for near-instant failover (HA-08, SC#3)

affects:
  - 07-03-role-gated-exporter (MetricRoleGatedExporter consumes ILeaderElection.IsLeader)
  - 07-04-di-wiring (registers K8sLeaseElection as singleton ILeaderElection + IHostedService)

# Tech tracking
tech-stack:
  added:
    - KubernetesClient 18.0.13 (k8s.LeaderElection, k8s.LeaderElection.ResourceLock namespaces)
  patterns:
    - BackgroundService + ILeaderElection dual interface pattern (same class satisfies both DI registrations)
    - Volatile bool single-writer/multi-reader pattern for cross-thread leadership flag
    - StopAsync override: base.StopAsync first (cancels stoppingToken), then explicit lease delete

key-files:
  created:
    - src/SnmpCollector/Telemetry/K8sLeaseElection.cs
  modified:
    - src/SnmpCollector/SnmpCollector.csproj

key-decisions:
  - "K8sLeaseElection ported directly from Simetra reference with namespace swap only — no structural differences"
  - "StopAsync calls base.StopAsync first to cancel ExecuteAsync before attempting lease delete — correct ordering prevents race on _isLeader flag"
  - "RenewDeadline = DurationSeconds - 2 (fixed offset) — matches Simetra reference; gives 2s window before TTL for retry"
  - "_lifetime field retained in constructor (not used in ExecuteAsync) — available for future shutdown coordination without breaking constructor signature"

patterns-established:
  - "HA failover pattern: StopAsync deletes Kubernetes lease when _isLeader=true, followers acquire immediately instead of waiting TTL"
  - "Identity resolution: PodIdentity ?? Environment.MachineName — PostConfigure in Plan 04 sets PodIdentity from HOSTNAME; MachineName is final fallback"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 7 Plan 02: K8sLeaseElection BackgroundService Summary

**Kubernetes Lease-based leader election BackgroundService using KubernetesClient 18.0.13, with volatile IsLeader flag and explicit lease deletion on SIGTERM for near-instant HA failover.**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-05T16:19:37Z
- **Completed:** 2026-03-05T16:21:50Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Added KubernetesClient 18.0.13 to SnmpCollector.csproj (provides k8s.LeaderElection namespace)
- Created K8sLeaseElection implementing BackgroundService + ILeaderElection with LeaseLock/LeaderElector loop
- StopAsync explicitly deletes the Kubernetes Lease resource when leader, enabling near-instant follower promotion (HA-08, SC#3)
- Build succeeds with 0 errors; all 102 existing tests still pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Add KubernetesClient NuGet package** - `6879e3f` (chore)
2. **Task 2: K8sLeaseElection BackgroundService implementing ILeaderElection** - `b639a93` (feat)

## Files Created/Modified

- `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` - Kubernetes Lease-based leader election BackgroundService; ExecuteAsync runs LeaseLock/LeaderElector loop with configurable timing; StopAsync deletes lease for near-instant failover
- `src/SnmpCollector/SnmpCollector.csproj` - Added KubernetesClient 18.0.13 PackageReference

## Decisions Made

- K8sLeaseElection ported directly from Simetra reference (`src/Simetra/Telemetry/K8sLeaseElection.cs`) with namespace swap (`SnmpCollector.Telemetry`) and comment updates referencing SnmpCollector types. No structural differences — the Simetra implementation is already the proven pattern.
- `StopAsync` calls `base.StopAsync(cancellationToken)` first (cancels the BackgroundService stoppingToken, stopping `ExecuteAsync`) before attempting the lease delete. This ordering ensures the election loop is not still running when the lease is deleted, preventing a race where the loop immediately re-acquires.
- `_lifetime` (IHostApplicationLifetime) retained in constructor for future shutdown coordination without breaking the constructor contract.
- `RenewDeadline = DurationSeconds - 2` matches Simetra reference: gives a 2-second grace window before TTL expiry for the elector to retry renewal internally.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- K8sLeaseElection is complete and compiles. It is NOT yet registered in DI (Plan 04 handles registration).
- Plan 03 (MetricRoleGatedExporter) can proceed immediately — it depends only on ILeaderElection (from Plan 01).
- Plan 04 DI wiring will register K8sLeaseElection as singleton satisfying both ILeaderElection and IHostedService, and PostConfigure SiteOptions.PodIdentity from HOSTNAME env var.
- No blockers for Plan 03 or Plan 04.

---
*Phase: 07-leader-election-and-role-gated-export*
*Completed: 2026-03-05*
