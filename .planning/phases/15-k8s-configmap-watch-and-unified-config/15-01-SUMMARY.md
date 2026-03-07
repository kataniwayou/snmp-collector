---
phase: 15
plan: 01
subsystem: configuration
tags: [config-model, oid-map, device-registry, mutable-registries, atomic-swap]
dependency-graph:
  requires: []
  provides: [SimetraConfigModel, mutable-OidMapService, mutable-DeviceRegistry, registry-cleanup-methods]
  affects: [15-02, 15-03, 15-04, 15-05]
tech-stack:
  added: []
  patterns: [volatile-atomic-swap, frozen-dictionary-reload]
file-tracking:
  key-files:
    created:
      - src/SnmpCollector/Configuration/SimetraConfigModel.cs
    modified:
      - src/SnmpCollector/Pipeline/OidMapService.cs
      - src/SnmpCollector/Pipeline/IOidMapService.cs
      - src/SnmpCollector/Pipeline/DeviceRegistry.cs
      - src/SnmpCollector/Pipeline/IDeviceRegistry.cs
      - src/SnmpCollector/Pipeline/JobIntervalRegistry.cs
      - src/SnmpCollector/Pipeline/IJobIntervalRegistry.cs
      - src/SnmpCollector/Pipeline/LivenessVectorService.cs
      - src/SnmpCollector/Pipeline/ILivenessVectorService.cs
      - tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs
      - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
      - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
      - tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs
      - tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs
      - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
decisions: []
metrics:
  duration: ~5 minutes
  completed: 2026-03-07
---

# Phase 15 Plan 01: Unified Config Model and Mutable Registries Summary

Unified SimetraConfigModel POCO plus mutable OidMapService/DeviceRegistry with volatile FrozenDictionary swap, and cleanup methods on JobIntervalRegistry/LivenessVectorService for runtime config reload.

## What Was Done

### Task 1: SimetraConfigModel and Mutable OidMapService + DeviceRegistry

- Created `SimetraConfigModel` with `OidMap` (Dictionary) and `Devices` (List) properties for unified ConfigMap deserialization.
- Refactored `OidMapService` to remove `IOptionsMonitor<OidMapOptions>` dependency entirely. Constructor now accepts `Dictionary<string, string> initialEntries` directly.
- Added `UpdateMap(Dictionary<string, string>)` method to OidMapService with atomic volatile FrozenDictionary swap and structured diff logging (added/removed/changed).
- Removed `IDisposable` implementation and `_changeToken` field from OidMapService (no more OnChange subscription).
- Made `DeviceRegistry._byIp` and `_byName` fields volatile instead of readonly.
- Added `ILogger<DeviceRegistry>` parameter to DeviceRegistry constructor.
- Added `ReloadAsync(List<DeviceOptions>)` method to DeviceRegistry with async DNS resolution and atomic FrozenDictionary swap. Returns added/removed device name sets for downstream cleanup.
- Updated `IOidMapService` and `IDeviceRegistry` interfaces with new methods.

### Task 2: Cleanup Methods and Test Updates

- Added `Unregister(string jobKey)` to `JobIntervalRegistry` / `IJobIntervalRegistry` for removing deleted job intervals.
- Added `Remove(string jobKey)` to `LivenessVectorService` / `ILivenessVectorService` for cleaning up liveness stamps.
- Refactored `OidMapServiceTests` to construct `OidMapService` directly with Dictionary (no more TestOptionsMonitor). Hot-reload tests now use `sut.UpdateMap()`.
- Added two new `DeviceRegistryTests`: `ReloadAsync_AddsNewDevice_FoundByName` and `ReloadAsync_RemovesDevice_NotFoundByName`.
- All 19 filtered tests pass.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed stub implementations in 4 test files for new interface members**

- **Found during:** Task 2
- **Issue:** Adding `UpdateMap` to `IOidMapService`, `Remove` to `ILivenessVectorService`, and `ReloadAsync` to `IDeviceRegistry` broke stub implementations in OidResolutionBehaviorTests, HeartbeatJobTests, LivenessHealthCheckTests, and MetricPollJobTests.
- **Fix:** Added no-op implementations of the new interface methods to each stub class, plus the required `using SnmpCollector.Configuration` in MetricPollJobTests.
- **Files modified:** 4 test files (OidResolutionBehaviorTests, HeartbeatJobTests, LivenessHealthCheckTests, MetricPollJobTests)
- **Commits:** 7a914c2

## Verification

- All 19 OidMapService + DeviceRegistry tests pass
- OidMapService.cs has no `using Microsoft.Extensions.Options` import
- DeviceRegistry.cs has `volatile` keyword on both dictionary fields
- IJobIntervalRegistry.cs contains `Unregister` method
- ILivenessVectorService.cs contains `Remove` method

## Next Phase Readiness

Plan 15-02 (ConfigMap watcher) can now call:
- `IOidMapService.UpdateMap()` to reload OID maps
- `IDeviceRegistry.ReloadAsync()` to reload devices with add/remove tracking
- `IJobIntervalRegistry.Unregister()` to clean up removed job intervals
- `ILivenessVectorService.Remove()` to clean up removed job liveness stamps

Note: `dotnet build` of the main project will fail until Plan 15-03 updates `ServiceCollectionExtensions.cs` to use the new OidMapService constructor signature. This is expected.
