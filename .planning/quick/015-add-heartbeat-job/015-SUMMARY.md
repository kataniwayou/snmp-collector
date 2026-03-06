---
phase: quick-015
plan: 01
subsystem: scheduling
tags: [quartz, heartbeat, snmp, liveness, loopback-trap]
dependency-graph:
  requires: [phase-06, phase-08]
  provides: [heartbeat-job, end-to-end-health-proof]
  affects: []
tech-stack:
  added: []
  patterns: [loopback-trap-health-check]
key-files:
  created:
    - src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
    - src/SnmpCollector/Jobs/HeartbeatJob.cs
    - tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
decisions: []
metrics:
  duration: ~3 min
  completed: 2026-03-06
---

# Quick Task 015: Add HeartbeatJob Summary

Loopback SNMPv2c heartbeat trap job proving scheduler and pipeline health end-to-end via CommunityStringHelper-derived community string.

## What Was Done

### Task 1: HeartbeatJobOptions and HeartbeatJob
- Created `HeartbeatJobOptions` with `SectionName = "HeartbeatJob"`, `HeartbeatOid` const, and `IntervalSeconds` default 15
- Created `HeartbeatJob` that sends loopback SNMPv2c trap to 127.0.0.1 on configured listener port
- Community string derived via `CommunityStringHelper.DeriveFromDeviceName("heartbeat")` (not hardcoded)
- Liveness vector stamped in finally block (always, even on failure)
- Correlation ID scoped at start, cleared in finally

### Task 2: DI and Quartz Wiring
- Bound `HeartbeatJobOptions` in `AddSnmpConfiguration` with `ValidateDataAnnotations` and `ValidateOnStart`
- Registered HeartbeatJob in Quartz with configurable interval trigger and misfire handling
- Incremented thread pool `jobCount` from 1 to 2 (CorrelationJob + HeartbeatJob)
- Registered "heartbeat" in `JobIntervalRegistry` for liveness staleness threshold

### Task 3: Unit Tests
- 5 tests covering: liveness stamping, correlation ID lifecycle, failure-path stamping, community string derivation, job key passthrough
- All 120 tests pass (115 existing + 5 new)

## Deviations from Plan

None -- plan executed exactly as written.

## Commits

| Hash | Message |
|------|---------|
| 038075e | feat(quick-015): add HeartbeatJobOptions and HeartbeatJob |
| f7250bb | feat(quick-015): wire HeartbeatJob into DI and Quartz scheduler |
| 1f5cd42 | test(quick-015): add HeartbeatJob unit tests |
