---
phase: 02-device-registry-and-oid-map
plan: 02
subsystem: pipeline
tags: [dotnet, pipeline, FrozenDictionary, IOptionsMonitor, hot-reload, DeviceRegistry, OidMapService, DI]

# Dependency graph
requires:
  - phase: 02-device-registry-and-oid-map/02-01
    provides: DevicesOptions, OidMapOptions, SnmpListenerOptions, DevicesOptionsValidator, SnmpListenerOptionsValidator
  - phase: 01-infrastructure-foundation/01-04
    provides: ServiceCollectionExtensions.AddSnmpConfiguration base, DI wiring pattern
provides:
  - DeviceInfo: immutable runtime record (Name, IpAddress, CommunityString, PollGroups)
  - MetricPollInfo: runtime poll group record with JobKey helper
  - IDeviceRegistry: TryGetDevice (IP trap path), TryGetDeviceByName (poll path), AllDevices
  - DeviceRegistry: FrozenDictionary<IPAddress> + FrozenDictionary<string> (OrdinalIgnoreCase), community string fallback
  - IOidMapService: Resolve(oid), EntryCount
  - OidMapService: volatile FrozenDictionary swap, IOptionsMonitor hot-reload, diff logging, IDisposable
  - ServiceCollectionExtensions.AddSnmpConfiguration: Phase 2 DI registrations added (DevicesOptions, SnmpListenerOptions, OidMapOptions, IDeviceRegistry, IOidMapService)
affects:
  - Phase 3 (MediatR pipeline): OidResolutionBehavior uses IOidMapService.Resolve(); OtelMetricHandler uses IDeviceRegistry
  - Phase 5 (Trap listener): TryGetDevice(senderIp) for per-trap device lookup
  - Phase 6 (Quartz poller): AllDevices + TryGetDeviceByName(deviceName) for job registration and execution
  - Phase 8 (Leader election): No changes needed -- DeviceRegistry/OidMapService are already singletons

# Tech tracking
tech-stack:
  added:
    - System.Collections.Frozen (built-in .NET 8+) -- FrozenDictionary for immutable O(1) lookups
  patterns:
    - "DeviceRegistry: FrozenDictionary built at startup, no locks needed (immutable after construction)"
    - "OidMapService: volatile FrozenDictionary field for lock-free atomic swap on hot-reload"
    - "IOptionsMonitor.OnChange returns IDisposable token -- OidMapService implements IDisposable to unsubscribe"
    - "Community string resolution: string.IsNullOrWhiteSpace(d.CommunityString) ? globalCommunity : d.CommunityString"
    - "DevicesOptions binding: Configure<IConfiguration> delegate (JSON array root -> opts.Devices list)"
    - "OidMapOptions binding: Configure<IConfiguration> delegate (JSON object -> opts.Entries dictionary)"

key-files:
  created:
    - src/SnmpCollector/Pipeline/DeviceInfo.cs
    - src/SnmpCollector/Pipeline/MetricPollInfo.cs
    - src/SnmpCollector/Pipeline/IDeviceRegistry.cs
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - src/SnmpCollector/Pipeline/IOidMapService.cs
    - src/SnmpCollector/Pipeline/OidMapService.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/appsettings.Development.json

key-decisions:
  - "DevicesOptions uses Configure<IConfiguration> delegate (not .Bind()) -- JSON 'Devices' is a top-level array; .Bind(GetSection('Devices')) binds array indices as property names on DevicesOptions, not to the Devices list"
  - "OidMapOptions uses Configure<IConfiguration> delegate -- binds section children (OID->name pairs) directly into the Entries dictionary"
  - "AllDevices returns _byIp.Values (not a separate list) -- values are device-ordered by IP dict insertion; adequate for Phase 6 scheduler"
  - "OidMapService diff logging logs every added/removed/changed OID entry at Information -- visible in Grafana log panels"
  - "DeviceRegistry and OidMapService both registered as singletons in AddSnmpConfiguration (not AddSnmpScheduling) -- they are config-tier services, not scheduling-tier"

patterns-established:
  - "FrozenDictionary pattern: build mutable dict in constructor, call .ToFrozenDictionary() at end -- zero allocations during lookup"
  - "Hot-reload pattern: volatile field + atomic swap in OnChange callback -- no reader lock needed for FrozenDictionary reads"
  - "Configure<IConfiguration> delegate for non-standard section shapes (arrays, flat dicts)"

# Metrics
duration: 6min
completed: 2026-03-05
---

# Phase 2 Plan 2: DeviceRegistry and OidMapService Summary

**FrozenDictionary-backed DeviceRegistry and hot-reloadable OidMapService with volatile atomic swap -- six pipeline files and updated DI wiring in AddSnmpConfiguration**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-03-05T00:08:44Z
- **Completed:** 2026-03-05T00:14:55Z
- **Tasks:** 2
- **Files modified:** 8 (6 created, 2 modified)

## Accomplishments

- Created six pipeline files: DeviceInfo (sealed record), MetricPollInfo (sealed record with JobKey), IDeviceRegistry (interface), DeviceRegistry (FrozenDictionary-backed singleton), IOidMapService (interface), OidMapService (volatile hot-reloadable singleton)
- DeviceRegistry resolves community strings at startup: per-device override wins, falls back to global SnmpListenerOptions.CommunityString
- OidMapService implements IDisposable to unregister IOptionsMonitor.OnChange token; diffs are logged at Information level (added/removed/changed per OID)
- Updated ServiceCollectionExtensions.AddSnmpConfiguration with all Phase 2 registrations (DevicesOptions, SnmpListenerOptions, OidMapOptions, IDeviceRegistry, IOidMapService)
- Added Development sample config: two devices (npb-core-01/10.0.10.1, obp-edge-01/10.0.10.2 with per-device community string), five OID map entries, SnmpListener section
- Application starts successfully with Development environment, structured logging with [site-nyc-01|standalone|correlationId] format

## Task Commits

Each task was committed atomically:

1. **Task 1: Create DeviceInfo, MetricPollInfo, IDeviceRegistry, DeviceRegistry, IOidMapService, OidMapService** - `3b5b3e2` (feat)
2. **Task 2: Wire Phase 2 options and services into DI container and add Development sample config** - `14cc9d0` (feat)

**Plan metadata:** (committed after SUMMARY creation)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/DeviceInfo.cs` - Sealed record: Name, IpAddress, CommunityString, PollGroups (IReadOnlyList<MetricPollInfo>)
- `src/SnmpCollector/Pipeline/MetricPollInfo.cs` - Sealed record: PollIndex, Oids, IntervalSeconds + JobKey(deviceName) -> "metric-poll-{deviceName}-{pollIndex}"
- `src/SnmpCollector/Pipeline/IDeviceRegistry.cs` - TryGetDevice(IPAddress), TryGetDeviceByName(string), AllDevices property
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` - FrozenDictionary<IPAddress, DeviceInfo> by IP, FrozenDictionary<string, DeviceInfo> OrdinalIgnoreCase by name; community string fallback; IOptions<DevicesOptions> + IOptions<SnmpListenerOptions>
- `src/SnmpCollector/Pipeline/IOidMapService.cs` - Resolve(oid) -> metricName or "Unknown", EntryCount property
- `src/SnmpCollector/Pipeline/OidMapService.cs` - volatile FrozenDictionary swap, IOptionsMonitor<OidMapOptions>.OnChange, diff logging, IDisposable; public const string Unknown = "Unknown"
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Phase 2 DI registrations: DevicesOptions (Configure<IConfiguration>), SnmpListenerOptions (Bind + DataAnnotations), OidMapOptions (Configure<IConfiguration>), IDeviceRegistry singleton, IOidMapService singleton
- `src/SnmpCollector/appsettings.Development.json` - Sample: npb-core-01 (2 poll groups), obp-edge-01 (per-device community, 1 poll group), 5 OID entries, SnmpListener section

## Decisions Made

- `DevicesOptions` uses `Configure<IConfiguration>((opts, cfg) => cfg.GetSection("Devices").Bind(opts.Devices))` -- the JSON `"Devices"` key is a top-level array; `AddOptions<T>().Bind(GetSection("Devices"))` tries to map array index keys (0,1,...) as property names on `DevicesOptions`, which silently fails. The delegate form binds the array section directly into the `List<DeviceOptions>` property.
- `OidMapOptions` uses the same delegate pattern -- `cfg.GetSection("OidMap").Bind(opts.Entries)` binds the flat JSON object (OID->name) into the `Dictionary<string,string>` directly.
- `AllDevices` returns `_byIp.Values` (not a separate ordered list) -- adequate for Phase 6 scheduler which just needs to enumerate all devices to register jobs; order doesn't matter for Quartz job registration.
- `OidMapService` diff logging logs each added/removed/changed OID at Information level (not Debug) -- these are config events that operators need visibility into without log level tuning.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] DevicesOptions Configure delegate binding**

- **Found during:** Task 2 verification (app startup test)
- **Issue:** The plan specified `.Bind(configuration.GetSection(DevicesOptions.SectionName))` for DevicesOptions. The `"Devices"` JSON key is a top-level array. Using `.Bind()` on an `OptionsBuilder<DevicesOptions>` passes the array section to the binder, which tries to map array index keys ("0", "1", ...) as property names on `DevicesOptions`. This silently leaves `Devices` empty -- no error, just no devices loaded.
- **Fix:** Used `Configure<IConfiguration>((opts, cfg) => cfg.GetSection(DevicesOptions.SectionName).Bind(opts.Devices))` which binds the array section directly into the `List<DeviceOptions>` property.
- **Files modified:** `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs`
- **Commit:** `14cc9d0`

**2. [Rule 3 - Blocking] dotnet run must be executed from project directory**

- **Found during:** Task 2 verification
- **Issue:** Running `dotnet run --project src/SnmpCollector` from repo root with `DOTNET_ENVIRONMENT=Development` failed with `SiteOptions.Name required` because `Host.CreateApplicationBuilder` uses `Directory.GetCurrentDirectory()` to locate `appsettings.json`, which was the repo root (no appsettings.json there).
- **Fix:** Run `dotnet run` from the project directory (`src/SnmpCollector/`) so `appsettings.json` and `appsettings.Development.json` are found correctly.
- **Files modified:** None (execution procedure only)
- **Commit:** N/A

## Issues Encountered

**Build file-lock (pre-existing):** `dotnet build -c Debug` fails with MSB3027 when the previous SnmpCollector.exe is running. Same as 02-01. Used `dotnet build -c Release` for clean build + `dotnet run` from project directory. Zero `error CS` compile errors confirmed.

## User Setup Required

None -- all new services use existing config infrastructure. Development sample config in `appsettings.Development.json` is ready to use.

## Next Phase Readiness

- `IDeviceRegistry` ready for Phase 5 trap listener (`TryGetDevice(senderIp)`) and Phase 6 Quartz poller (`AllDevices`, `TryGetDeviceByName`)
- `IOidMapService.Resolve(oid)` ready for Phase 3 `OidResolutionBehavior`
- `MetricPollInfo.JobKey(deviceName)` ready for Phase 6 Quartz job registration
- `OidMapService.Unknown` constant ready for Phase 3 behavior
- No blockers

---
*Phase: 02-device-registry-and-oid-map*
*Completed: 2026-03-05*
