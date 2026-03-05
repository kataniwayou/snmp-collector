---
phase: 02-device-registry-and-oid-map
plan: 01
subsystem: infra
tags: [dotnet, options, configuration, IValidateOptions, DeviceOptions, OidMap, SnmpListener]

# Dependency graph
requires:
  - phase: 01-infrastructure-foundation/01-05
    provides: IValidateOptions validator pattern, AddSnmpConfiguration wiring, fail-fast "Section:Field is required" message format
provides:
  - DeviceOptions: Name, IpAddress, CommunityString? (nullable per-device override), MetricPolls
  - MetricPollOptions: Oids (List<string>) and IntervalSeconds only -- no MetricName/MetricType
  - DevicesOptions: wraps List<DeviceOptions> with SectionName="Devices"
  - OidMapOptions: Dictionary<string, string> Entries with SectionName="OidMap"
  - SnmpListenerOptions: BindAddress, Port, CommunityString (global default), Version with DataAnnotations
  - DevicesOptionsValidator: validates nested device graph (Name, IpAddress format, duplicates, MetricPoll Oids/IntervalSeconds)
  - SnmpListenerOptionsValidator: validates BindAddress, CommunityString, Version=v2c, Port range
affects:
  - 02-02 (DeviceRegistry): depends on DeviceOptions, DevicesOptions, DevicesOptionsValidator registration
  - 02-03 (OidMapService): depends on OidMapOptions
  - 02-04 (DI wiring): registers all options and validators in AddSnmpConfiguration
  - Phase 3 (MediatR pipeline): OidResolutionBehavior uses OidMapService; MetricPollJob uses DeviceRegistry
  - Phase 5 (Quartz scheduling): MetricPollJob identity "metric-poll-{deviceName}-{pollIndex}" derivable from DeviceOptions.Name

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Device config: Name + IpAddress + CommunityString? (nullable per-device override) + List<MetricPollOptions> -- no DeviceType"
    - "Poll group: List<string> Oids + int IntervalSeconds -- flat OID strings, no OidEntryOptions wrapper"
    - "OID map: Dictionary<string, string> in OidMapOptions.Entries -- supports IOptionsMonitor hot-reload"
    - "DevicesOptionsValidator: walks nested graph manually (Devices -> MetricPolls -> Oids count) because ValidateDataAnnotations skips nested objects"
    - "IP address validation via IPAddress.TryParse in DevicesOptionsValidator"

key-files:
  created:
    - src/SnmpCollector/Configuration/DeviceOptions.cs
    - src/SnmpCollector/Configuration/MetricPollOptions.cs
    - src/SnmpCollector/Configuration/DevicesOptions.cs
    - src/SnmpCollector/Configuration/OidMapOptions.cs
    - src/SnmpCollector/Configuration/SnmpListenerOptions.cs
    - src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
    - src/SnmpCollector/Configuration/Validators/SnmpListenerOptionsValidator.cs
  modified: []

key-decisions:
  - "DeviceOptions.CommunityString is nullable string? (not required) -- per-device override, falls back to SnmpListenerOptions.CommunityString when null/empty"
  - "MetricPollOptions.Oids is List<string> (plain OID strings) -- no OidEntryOptions wrapper; TypeCode resolved at runtime from SNMP response"
  - "No DeviceType on DeviceOptions -- SnmpCollector is device-agnostic, flat OID map replaces device modules"
  - "IPAddress.TryParse used in DevicesOptionsValidator for IP format check -- catches hostname strings early at startup"

patterns-established:
  - "Validator pattern continues: public sealed class XxxValidator : IValidateOptions<XxxOptions> in SnmpCollector.Configuration.Validators namespace"
  - "Nested validation: DevicesOptionsValidator manually walks Devices[i].MetricPolls[j] because DataAnnotations skips nested objects"
  - "Duplicate detection with HashSet<string>(StringComparer.OrdinalIgnoreCase) for case-insensitive device name/IP uniqueness"

# Metrics
duration: 4min
completed: 2026-03-05
---

# Phase 2 Plan 1: Configuration Options Classes Summary

**Five options POCOs and two IValidateOptions validators for device registry and OID map -- no DeviceType, flat OID strings, nullable per-device CommunityString override**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-03-05T00:01:33Z
- **Completed:** 2026-03-05T00:05:25Z
- **Tasks:** 2
- **Files modified:** 7 (all created)

## Accomplishments
- Created all five options classes: DeviceOptions (CommunityString? override), MetricPollOptions (List<string> Oids + IntervalSeconds), DevicesOptions, OidMapOptions (Dictionary<string,string>), SnmpListenerOptions (DataAnnotations on all four fields)
- Created DevicesOptionsValidator: walks full nested graph, validates Name/IpAddress required, IPAddress.TryParse format check, duplicate name/IP detection, MetricPolls IntervalSeconds > 0 and Oids non-empty
- Created SnmpListenerOptionsValidator: four checks matching 01-05 "Section:Field is required" message format
- Zero C# compile errors (file-lock MSB3027 on running exe is not a compile error)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create the five configuration options classes** - `b5b0819` (feat)
2. **Task 2: Create DevicesOptionsValidator and SnmpListenerOptionsValidator** - `9efda39` (feat)

**Plan metadata:** (committed after SUMMARY creation)

## Files Created/Modified
- `src/SnmpCollector/Configuration/DeviceOptions.cs` - Name, IpAddress, CommunityString? (nullable), List<MetricPollOptions> MetricPolls
- `src/SnmpCollector/Configuration/MetricPollOptions.cs` - List<string> Oids and int IntervalSeconds only; Quartz job identity documented in class XML doc
- `src/SnmpCollector/Configuration/DevicesOptions.cs` - Wraps List<DeviceOptions> with SectionName="Devices"
- `src/SnmpCollector/Configuration/OidMapOptions.cs` - Dictionary<string, string> Entries with SectionName="OidMap"
- `src/SnmpCollector/Configuration/SnmpListenerOptions.cs` - BindAddress, Port, CommunityString (global default), Version with [Required]/[Range]/[RegularExpression]
- `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` - IValidateOptions<DevicesOptions> nested graph walker
- `src/SnmpCollector/Configuration/Validators/SnmpListenerOptionsValidator.cs` - IValidateOptions<SnmpListenerOptions> four-field validator

## Decisions Made
- `DeviceOptions.CommunityString` is `string?` nullable -- per-device override pattern; null/empty means fall back to global `SnmpListenerOptions.CommunityString`. No validation needed on this field since it's intentionally optional.
- `MetricPollOptions.Oids` is `List<string>` (plain OID strings, not `OidEntryOptions`) -- SnmpCollector is device-agnostic; TypeCode determined at runtime from SNMP GET response, not config.
- `DeviceType` removed entirely from `DeviceOptions` -- no device module system, flat OID map is device-agnostic.
- `IPAddress.TryParse` used in `DevicesOptionsValidator` for IP format check -- catches hostnames and typos at startup before any SNMP session attempts.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

**Build file-lock warning:** `dotnet build` reported MSB3027 (cannot copy SnmpCollector.exe -- file in use by PID 27872). This is not a C# compile error; the running process holds the output exe. Zero `error CS` compile errors confirmed by filtering build output. All C# code compiled cleanly.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All options POCOs ready for 02-02 (DeviceRegistry) and 02-03 (OidMapService) to consume
- Both validators ready for 02-04 DI wiring (AddSingleton<IValidateOptions<T>, TValidator>)
- DevicesOptions.SectionName and OidMapOptions.SectionName defined -- 02-04 can bind with services.Configure<T>(config.GetSection(T.SectionName))
- No blockers

---
*Phase: 02-device-registry-and-oid-map*
*Completed: 2026-03-05*
