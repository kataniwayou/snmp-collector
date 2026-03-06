# Phase 10 Plan 07: Per-Device Port and CommunityString Summary

**One-liner:** Per-device Port/CommunityString in config model with Simetra.* validation, MetricPollJob uses them directly (no sysUpTime prepend)

## What Was Done

### Task 1: Add Port and CommunityString to config model, DeviceInfo, DeviceRegistry, and validator
- Added `Port` (int, default 161) and `CommunityString` (string, required) to `DeviceOptions`
- Extended `DeviceInfo` record with `Port` and `CommunityString` parameters
- Updated `DeviceRegistry` to pass `d.Port` and `d.CommunityString` when constructing `DeviceInfo`
- Added validation in `DevicesOptionsValidator`: Port 1-65535, CommunityString required and must start with "Simetra." (Ordinal)
- Updated appsettings.Development.json and K8s configmap with CommunityString values
- Updated DeviceRegistryTests and PipelineIntegrationTests DeviceOptions with CommunityString
- **Commit:** 922d75f

### Task 2: Update MetricPollJob and all tests for new DeviceInfo, remove sysUpTime
- Removed `SysUpTimeOid` constant and sysUpTime prepend from variable list
- Changed endpoint from hardcoded port 161 to `device.Port`
- Changed community from `CommunityStringHelper.DeriveFromDeviceName(device.Name)` to `device.CommunityString`
- Updated MetricPollJobTests: removed SysUpTimeOid, updated MakeDevice with Port/CommunityString
- Replaced Test 3 (sysUpTime dispatch) with test verifying device.Port and device.CommunityString are used
- Added LastEndpoint/LastCommunity capture to StubSnmpClient
- All 115 tests pass
- **Commit:** 4c098cf

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added CommunityString to appsettings.Development.json**
- **Found during:** Task 1
- **Issue:** Development config had device entries without CommunityString; would fail validation at startup
- **Fix:** Added `"CommunityString": "Simetra.npb-core-01"` and `"Simetra.obp-edge-01"` to dev config devices
- **Files modified:** src/SnmpCollector/appsettings.Development.json

**2. [Rule 2 - Missing Critical] Added CommunityString to K8s snmp-collector configmap**
- **Found during:** Task 1
- **Issue:** K8s dummy device entry lacked CommunityString; would fail validation in cluster
- **Fix:** Added `"CommunityString": "Simetra.dummy-device-01"` to configmap
- **Files modified:** deploy/k8s/snmp-collector/configmap.yaml

**3. [Rule 2 - Missing Critical] Updated DeviceRegistryTests with CommunityString**
- **Found during:** Task 1
- **Issue:** DeviceRegistryTests constructed DeviceOptions without CommunityString
- **Fix:** Added CommunityString to both test device options
- **Files modified:** tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs

## Verification

- `dotnet build` -- 0 errors, 0 warnings
- `dotnet test` -- 115 tests passed
- `grep SysUpTimeOid src/SnmpCollector/Jobs/MetricPollJob.cs` -- 0 hits
- `grep DeriveFromDeviceName src/SnmpCollector/Jobs/MetricPollJob.cs` -- 0 hits

## Files Modified

### Created
- (none)

### Modified
- src/SnmpCollector/Configuration/DeviceOptions.cs
- src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
- src/SnmpCollector/Pipeline/DeviceInfo.cs
- src/SnmpCollector/Pipeline/DeviceRegistry.cs
- src/SnmpCollector/Jobs/MetricPollJob.cs
- src/SnmpCollector/appsettings.Development.json
- deploy/k8s/snmp-collector/configmap.yaml
- tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
- tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
- tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| CommunityString validated with StringComparison.Ordinal | Simetra.* prefix is ASCII; ordinal is fastest and correct |
| Port default 161 in DeviceOptions | Standard SNMP port; most devices won't override |
| sysUpTime prepend removed entirely | Configured OIDs only; sysUpTime can be added to OID list if needed |
| CommunityStringHelper.DeriveFromDeviceName no longer used by MetricPollJob | CommunityString is explicit per-device config now |

## Duration

~3 minutes
