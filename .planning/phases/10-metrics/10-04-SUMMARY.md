---
phase: "10-metrics"
plan: "04"
subsystem: "pipeline-integration"
tags: ["community-string", "di-wiring", "health-check", "graceful-shutdown", "validation"]
dependency-graph:
  requires: ["10-01", "10-02", "10-03"]
  provides: ["compilable-main-project", "ITrapChannel-DI", "convention-community-strings"]
  affects: ["10-05"]
tech-stack:
  added: []
  patterns: ["community-string-convention-in-poll-path", "hostname-log-enrichment"]
file-tracking:
  key-files:
    created: []
    modified:
      - "src/SnmpCollector/Jobs/MetricPollJob.cs"
      - "src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs"
      - "src/SnmpCollector/HealthChecks/ReadinessHealthCheck.cs"
      - "src/SnmpCollector/Lifecycle/GracefulShutdownService.cs"
      - "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
decisions:
  - id: "10-04-01"
    description: "ValidationBehavior rejects null DeviceName as programming error (not normal trap flow)"
  - id: "10-04-02"
    description: "ReadinessHealthCheck resolves SnmpTrapListenerService via IHostedService enumeration for IsBound check"
  - id: "10-04-03"
    description: "Log enrichment uses HOSTNAME env var / MachineName instead of SiteOptions.Name"
metrics:
  duration: "~5 min"
  completed: "2026-03-06"
---

# Phase 10 Plan 04: Poll Path Alignment, Readiness Check, DI Wiring, and Build Fix Summary

**One-liner:** Poll path uses Simetra.{DeviceName} community convention, readiness checks trap listener IsBound, DI wires ITrapChannel, main project compiles cleanly.

## What Was Done

### Task 1: MetricPollJob, ValidationBehavior, ReadinessHealthCheck, GracefulShutdownService (e3aec51)

**MetricPollJob.cs:**
- Replaced `device.CommunityString` with `CommunityStringHelper.DeriveFromDeviceName(device.Name)` -- aligns poll path with the Simetra.{DeviceName} community string convention established in Plan 01.

**ValidationBehavior.cs:**
- Removed `IDeviceRegistry` dependency entirely -- no longer needed since both traps and polls always set DeviceName before entering the pipeline.
- Simplified null DeviceName check to reject with "MissingDeviceName" reason (programming error, not normal flow).

**ReadinessHealthCheck.cs:**
- Replaced `IDeviceChannelManager` dependency with `IServiceProvider` to resolve `SnmpTrapListenerService`.
- Health check now verifies `SnmpTrapListenerService.IsBound` (UDP socket active) instead of requiring devices configured.
- Quartz scheduler check retained unchanged.

**GracefulShutdownService.cs:**
- Replaced `IDeviceChannelManager _channelManager` with `ITrapChannel _trapChannel`.
- Shutdown step 4 calls `_trapChannel.Complete()` and `_trapChannel.WaitForDrainAsync()` instead of `_channelManager.CompleteAll()` / `WaitForDrainAsync()`.

### Task 2: ServiceCollectionExtensions DI Wiring Update (79d0699)

- Replaced `IDeviceChannelManager, DeviceChannelManager` registration with `ITrapChannel, TrapChannel`.
- Log enrichment processor now derives site name from `HOSTNAME` env var (falling back to `MachineName`) instead of `SiteOptions.Name`.
- Updated comments to reflect Phase 10 changes.
- Build verified: zero errors, zero warnings.

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| 10-04-01 | Null DeviceName is a programming error, not normal flow | Both traps (community string extraction) and polls (JobDataMap) always set DeviceName before pipeline entry |
| 10-04-02 | ReadinessHealthCheck resolves trap listener via IHostedService enumeration | Avoids adding a new interface just for health check access to IsBound property |
| 10-04-03 | Log enrichment uses HOSTNAME/MachineName instead of SiteOptions.Name | SiteOptions.Name is now optional (nullable); hostname provides consistent identity without config dependency |

## Verification Results

- `dotnet build src/SnmpCollector/SnmpCollector.csproj`: 0 errors, 0 warnings
- `IDeviceChannelManager` in src/SnmpCollector/: zero code references (only comments)
- `device.CommunityString` in src/SnmpCollector/: zero matches
- `siteOptions.Name` in src/SnmpCollector/: zero matches
- `DeviceNames.Count` in ReadinessHealthCheck: zero matches

## Deviations from Plan

None -- plan executed exactly as written.

## Next Phase Readiness

Plan 05 (test updates) is unblocked. The main project compiles cleanly. Test project will have compilation errors due to references to old types (IDeviceChannelManager, CommunityString on DeviceInfo, ValidationBehavior constructor with IDeviceRegistry) -- these are expected and will be fixed in Plan 05.
