# Phase 14 Plan 01: DNS Resolution and CommunityString Support Summary

**One-liner:** DNS resolution fallback in DeviceRegistry/MetricPollJob for K8s Service DNS names, optional CommunityString override, and devices.json auto-scan loading.

## What Was Done

### Task 1: Add CommunityString to DeviceOptions and DNS resolution to DeviceRegistry
- Added `CommunityString` optional property to `DeviceOptions.cs`
- Added `CommunityString` optional parameter to `DeviceInfo` record (with default null for backward compat)
- Replaced `IPAddress.Parse()` in `DeviceRegistry` with `TryParse` + `Dns.GetHostAddresses` fallback
- DeviceRegistry stores resolved IP string in `DeviceInfo.IpAddress` so downstream code uses raw IPs
- **Commit:** `1bd4443`

### Task 2: MetricPollJob CommunityString override, devices.json loading, tests
- MetricPollJob uses explicit `CommunityString` when available, falls back to `Simetra.{Name}` convention
- Program.cs auto-scans `devices.json` from CONFIG_DIRECTORY alongside `oidmap-*.json` files
- Added 3 DeviceRegistry tests: DNS hostname resolution, CommunityString passthrough, null fallback
- Added 1 MetricPollJob test: explicit CommunityString used instead of convention derivation
- **Commit:** `c6ad7c2`

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build` zero errors | PASS |
| `dotnet test` all 130 tests pass | PASS |
| DeviceOptions has CommunityString property | PASS |
| DeviceRegistry contains GetHostAddresses | PASS |
| Program.cs contains devices.json loading | PASS |

## Deviations from Plan

None - plan executed exactly as written.

## Key Files

### Created
None.

### Modified
- `src/SnmpCollector/Configuration/DeviceOptions.cs` - CommunityString property
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` - CommunityString parameter
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` - DNS resolution fallback
- `src/SnmpCollector/Jobs/MetricPollJob.cs` - CommunityString override logic
- `src/SnmpCollector/Program.cs` - devices.json auto-scan loading
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` - 3 new tests
- `tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs` - 1 new test

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| DeviceInfo CommunityString uses default parameter (null) | Backward compatibility -- existing code constructs DeviceInfo without CommunityString and continues to work |
| DNS resolution at startup (not per-poll) | Resolved IP stored in DeviceInfo; avoids DNS lookup on every poll cycle; K8s DNS is stable for Services |
| devices.json loaded after oidmap-*.json | Follows existing pattern; configuration layering is additive |

## Duration

Started: 2026-03-07T17:27:33Z
Completed: 2026-03-07
Tasks: 2/2
