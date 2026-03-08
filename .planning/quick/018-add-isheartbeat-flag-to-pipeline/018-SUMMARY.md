---
phase: quick-018
plan: 01
subsystem: pipeline
tags: [heartbeat, pipeline, refactor, boolean-flag]
dependency-graph:
  requires: []
  provides: [IsHeartbeat-flag, HeartbeatDeviceName-const]
  affects: []
tech-stack:
  added: []
  patterns: [boolean-flag-at-ingestion-boundary]
file-tracking:
  key-files:
    created: []
    modified:
      - src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
      - src/SnmpCollector/Pipeline/SnmpOidReceived.cs
      - src/SnmpCollector/Services/ChannelConsumerService.cs
      - src/SnmpCollector/Jobs/HeartbeatJob.cs
      - src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
      - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
      - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
      - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
decisions: []
metrics:
  duration: ~5 minutes
  completed: 2026-03-08
---

# Quick Task 018: Add IsHeartbeat Flag to Pipeline Summary

**One-liner:** Boolean IsHeartbeat flag set at ingestion boundary replaces scattered "heartbeat" string comparisons across pipeline behaviors and handlers.

## What Was Done

### Task 1: Add HeartbeatDeviceName const and IsHeartbeat property
- Added `HeartbeatDeviceName` const to `HeartbeatJobOptions` as single source of truth for the "heartbeat" device name string
- Added `IsHeartbeat` bool property (init-only) to `SnmpOidReceived`
- Set `IsHeartbeat` at ingestion boundary in `ChannelConsumerService` via case-insensitive string comparison against the const
- Replaced hardcoded `"heartbeat"` string in `HeartbeatJob` with `HeartbeatJobOptions.HeartbeatDeviceName`

### Task 2: Update pipeline behaviors to use IsHeartbeat
- `OidResolutionBehavior` now skips `Resolve()` call when `IsHeartbeat` is true, eliminating misleading "OID not found" log entries for heartbeat traffic
- `OtelMetricHandler` uses `notification.IsHeartbeat` flag instead of `string.Equals(deviceName, HeartbeatDeviceName, ...)` comparison
- Removed `HeartbeatDeviceName` const from `OtelMetricHandler` (consolidated into `HeartbeatJobOptions`)

### Task 3: Update tests for IsHeartbeat behavior
- Added `SkipsResolution_WhenIsHeartbeat` test confirming OidResolutionBehavior skips Resolve() and still calls next()
- Updated `Heartbeat_SkipsMetricRecording_ButIncrementsHandled` test to use `IsHeartbeat = true` flag
- Added `HeartbeatDeviceName_WithoutFlag_StillRecordsMetric` test proving the flag (not the string) controls suppression
- All 138 tests pass

## Deviations from Plan

None - plan executed exactly as written.

## Verification Results

1. `dotnet build` - 0 errors, 2 pre-existing warnings (K8s watcher deprecation)
2. `dotnet test` - 138 passed, 0 failed, 0 skipped
3. No hardcoded "heartbeat" device name strings remain in production code outside HeartbeatJobOptions (Quartz job keys are distinct identifiers, not device name comparisons)
4. `IsHeartbeat` used in: SnmpOidReceived, OidResolutionBehavior, OtelMetricHandler, ChannelConsumerService

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | da0fee8 | feat(quick-018): add HeartbeatDeviceName const and IsHeartbeat property |
| 2 | 7422810 | feat(quick-018): update pipeline behaviors to use IsHeartbeat flag |
| 3 | 5d0f980 | test(quick-018): add and update tests for IsHeartbeat flag behavior |
