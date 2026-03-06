---
phase: "10-metrics"
plan: "01"
subsystem: "configuration"
tags: ["config", "community-string", "device-registry", "cleanup"]
dependency_graph:
  requires: []
  provides: ["config-cleanup", "community-string-helper", "device-info-v2"]
  affects: ["10-02", "10-03", "10-04", "10-05"]
tech_stack:
  added: []
  patterns: ["Simetra.{DeviceName} community string convention"]
file_tracking:
  created:
    - "src/SnmpCollector/Pipeline/CommunityStringHelper.cs"
  modified:
    - "src/SnmpCollector/Configuration/SiteOptions.cs"
    - "src/SnmpCollector/Configuration/SnmpListenerOptions.cs"
    - "src/SnmpCollector/Configuration/DeviceOptions.cs"
    - "src/SnmpCollector/Configuration/Validators/SiteOptionsValidator.cs"
    - "src/SnmpCollector/Configuration/Validators/SnmpListenerOptionsValidator.cs"
    - "src/SnmpCollector/Pipeline/DeviceInfo.cs"
    - "src/SnmpCollector/Pipeline/DeviceRegistry.cs"
    - "src/SnmpCollector/appsettings.json"
    - "src/SnmpCollector/appsettings.Development.json"
decisions: []
metrics:
  duration: "~3 min"
  completed: "2026-03-06"
---

# Phase 10 Plan 01: Config Cleanup and Community String Convention Foundation Summary

Config cleanup removing legacy CommunityString fields from options/validators/DeviceInfo/DeviceRegistry, making SiteOptions.Name optional, creating CommunityStringHelper for Simetra.{DeviceName} convention.

## What Was Done

### Task 1: Config class cleanup and CommunityStringHelper (2993185)

- Made `SiteOptions.Name` optional (`string?`, removed `[Required]` and `required`)
- Removed `CommunityString` property from `SnmpListenerOptions`
- Removed `CommunityString` property from `DeviceOptions`
- Removed Name validation from `SiteOptionsValidator` (returns Success unconditionally)
- Removed CommunityString validation from `SnmpListenerOptionsValidator`
- Created `CommunityStringHelper` with `TryExtractDeviceName` and `DeriveFromDeviceName`

### Task 2: DeviceInfo, DeviceRegistry, and appsettings updates (13f50ae)

- Removed `CommunityString` parameter from `DeviceInfo` record (now 3 params: Name, IpAddress, PollGroups)
- Removed `IOptions<SnmpListenerOptions>` dependency from `DeviceRegistry` constructor
- Removed community string resolution logic (global fallback + per-device override)
- Cleaned `appsettings.json`: removed `Site` section and `CommunityString` from SnmpListener
- Cleaned `appsettings.Development.json`: removed `CommunityString` from device entry and SnmpListener

## Deviations from Plan

None -- plan executed exactly as written.

## Known Build State

Project does NOT build after this plan. Expected downstream errors in:
- `MetricPollJob.cs` (references `DeviceInfo.CommunityString`)
- `SnmpTrapListenerService.cs` (references `DeviceInfo.CommunityString`)
- `ServiceCollectionExtensions.cs` (passes `IOptions<SnmpListenerOptions>` to DeviceRegistry)

These will be resolved in Plans 02-04 which update callers to use `CommunityStringHelper.DeriveFromDeviceName`.

## Verification

- `CommunityStringHelper.TryExtractDeviceName("Simetra.npb-core-01", out name)` -> true, name = "npb-core-01"
- `CommunityStringHelper.TryExtractDeviceName("public", out _)` -> false
- `CommunityStringHelper.DeriveFromDeviceName("npb-core-01")` -> "Simetra.npb-core-01"
- SiteOptions.Name is `string?` with no `[Required]` attribute
- DeviceInfo record has 3 parameters (Name, IpAddress, PollGroups)

## Next Phase Readiness

Plans 02-05 can proceed. Plan 02 should update MetricPollJob and SnmpTrapListenerService to use CommunityStringHelper. Plan 04 should update ServiceCollectionExtensions DI wiring.
