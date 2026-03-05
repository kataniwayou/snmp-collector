---
phase: 02-device-registry-and-oid-map
verified: 2026-03-05T00:00:00Z
status: passed
score: 5/5 must-haves verified
re_verification: false
---
# Phase 2: Device Registry and OID Map -- Verification Report

**Phase Goal:** All lookup structures (device registry and OID map) are populated from configuration, O(1) lookups work correctly, cardinality is explicitly counted and bounded before any metric instruments are created, and hot-reload of the OID map functions without restart.
**Verified:** 2026-03-05
**Status:** passed
**Re-verification:** No -- initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Device can be looked up by IP in O(1) (trap path) | VERIFIED | DeviceRegistry uses FrozenDictionary<IPAddress, DeviceInfo> with MapToIPv4() normalization; TryGetDevice delegates to _byIp.TryGetValue(senderIp.MapToIPv4(), out device) |
| 2 | Device can be looked up by name in O(1) (poll path) | VERIFIED | DeviceRegistry uses FrozenDictionary<string, DeviceInfo> with StringComparer.OrdinalIgnoreCase; confirmed in unit test including NPB-CORE-01 case-insensitive lookup |
| 3 | OID in map resolves to metric_name; absent OID resolves to Unknown | VERIFIED | OidMapService.Resolve() returns _map.TryGetValue(oid, out var name) ? name : Unknown; public const string Unknown = "Unknown"; 6 unit tests pass covering known, unknown, and empty-map scenarios |
| 4 | Modifying OID map takes effect without restart | VERIFIED | OidMapService takes IOptionsMonitor<OidMapOptions>, calls monitor.OnChange(OnOidMapChanged), stores IDisposable token, performs atomic volatile field swap of FrozenDictionary; 2 hot-reload unit tests pass |
| 5 | Label taxonomy documented with cardinality estimate; all label values bounded | VERIFIED | CardinalityAuditService implements IHostedLifecycleService.StartingAsync; logs cardinality audit (devices x OIDs x instruments x sources) and label taxonomy (site_name/metric_name/oid/agent/source with bounded-value notes); warns at >10,000 series |
| 5b | Quartz job identities derivable from device config at startup | VERIFIED | MetricPollInfo.JobKey(string deviceName) returns metric-poll-{deviceName}-{PollIndex}; unit test asserts "metric-poll-npb-core-01-0" |

**Score:** 5/5 truths verified

---

### Required Artifacts

| Artifact | Exists | Lines | Substantive | Wired | Status |
|----------|--------|-------|-------------|-------|--------|
| src/SnmpCollector/Configuration/DeviceOptions.cs | YES | 32 | Name, IpAddress, CommunityString? nullable, MetricPolls -- no DeviceType | Used by validator and DeviceRegistry | VERIFIED |
| src/SnmpCollector/Configuration/MetricPollOptions.cs | YES | 19 | List<string> Oids + int IntervalSeconds only | Used by DeviceOptions and validator | VERIFIED |
| src/SnmpCollector/Configuration/DevicesOptions.cs | YES | 18 | SectionName="Devices", List<DeviceOptions> Devices | Bound in DI; consumed by DeviceRegistry | VERIFIED |
| src/SnmpCollector/Configuration/OidMapOptions.cs | YES | 18 | SectionName="OidMap", Dictionary<string,string> Entries | Bound via Configure delegate; consumed by OidMapService | VERIFIED |
| src/SnmpCollector/Configuration/SnmpListenerOptions.cs | YES | 37 | BindAddress, Port, CommunityString, Version with [Required]/[Range]/[RegularExpression] | Bound in DI; consumed by DeviceRegistry | VERIFIED |
| src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs | YES | 90 | Full nested walk: Name, IpAddress required, IPAddress.TryParse format, duplicate detection, MetricPolls nesting | Registered as IValidateOptions<DevicesOptions> singleton | VERIFIED |
| src/SnmpCollector/Configuration/Validators/SnmpListenerOptionsValidator.cs | YES | 38 | All 4 checks: BindAddress, CommunityString, Version=v2c, Port range | Registered as IValidateOptions<SnmpListenerOptions> singleton | VERIFIED |
| src/SnmpCollector/Pipeline/DeviceInfo.cs | YES | 16 | Sealed record: Name, IpAddress, CommunityString, PollGroups | Built by DeviceRegistry; used in CardinalityAuditService and tests | VERIFIED |
| src/SnmpCollector/Pipeline/MetricPollInfo.cs | YES | 21 | Sealed record: PollIndex, Oids, IntervalSeconds + JobKey() method | Built by DeviceRegistry; used in CardinalityAuditService and tests | VERIFIED |
| src/SnmpCollector/Pipeline/IDeviceRegistry.cs | YES | 37 | TryGetDevice(IPAddress), TryGetDeviceByName(string), AllDevices | Implemented by DeviceRegistry; injected into CardinalityAuditService | VERIFIED |
| src/SnmpCollector/Pipeline/DeviceRegistry.cs | YES | 78 | FrozenDictionary x2, MapToIPv4, OrdinalIgnoreCase, community string fallback logic | AddSingleton<IDeviceRegistry, DeviceRegistry> in DI | VERIFIED |
| src/SnmpCollector/Pipeline/IOidMapService.cs | YES | 22 | Resolve(oid), EntryCount | Implemented by OidMapService; injected into CardinalityAuditService | VERIFIED |
| src/SnmpCollector/Pipeline/OidMapService.cs | YES | 95 | volatile FrozenDictionary field, OnChange subscription, atomic swap, diff logging, IDisposable | AddSingleton<IOidMapService, OidMapService> in DI | VERIFIED |
| src/SnmpCollector/Pipeline/CardinalityAuditService.cs | YES | 106 | IHostedLifecycleService, StartingAsync calls AuditCardinality(), label taxonomy log, warn >10k series | AddHostedService<CardinalityAuditService> in DI | VERIFIED |
| tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj | YES | 25 | xunit 2.9.3, ProjectReference to SnmpCollector | Used by dotnet test | VERIFIED |
| tests/SnmpCollector.Tests/Helpers/TestOptionsMonitor.cs | YES | 34 | IOptionsMonitor<T> with Change() that fires OnChange synchronously | Used by OidMapServiceTests | VERIFIED |
| tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs | YES | 115 | 6 [Fact] methods: known, unknown, empty, EntryCount, reload-add, reload-remove | All 16 tests pass | VERIFIED |
| tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs | YES | 177 | 10 [Fact] methods: IP lookup, IPv6-mapped, miss, name exact, case-insensitive, miss, AllDevices, community fallback/override, JobKey | All 16 tests pass | VERIFIED |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| DeviceRegistry.cs | DevicesOptions.cs | IOptions<DevicesOptions> constructor parameter | WIRED | DeviceRegistry(IOptions<DevicesOptions> devicesOptions, ...) |
| DeviceRegistry.cs | SnmpListenerOptions.cs | IOptions<SnmpListenerOptions> constructor parameter | WIRED | Community fallback: string.IsNullOrWhiteSpace(d.CommunityString) ? globalCommunity : d.CommunityString |
| OidMapService.cs | OidMapOptions.cs | IOptionsMonitor<OidMapOptions> + OnChange callback | WIRED | _changeToken = monitor.OnChange(OnOidMapChanged); volatile _map = newMap atomic swap |
| ServiceCollectionExtensions.cs | DeviceRegistry.cs | AddSingleton<IDeviceRegistry, DeviceRegistry>() | WIRED | Line 202 of ServiceCollectionExtensions.cs |
| ServiceCollectionExtensions.cs | OidMapService.cs | AddSingleton<IOidMapService, OidMapService>() | WIRED | Line 203 of ServiceCollectionExtensions.cs |
| ServiceCollectionExtensions.cs | CardinalityAuditService.cs | AddHostedService<CardinalityAuditService>() | WIRED | Line 208 of ServiceCollectionExtensions.cs |
| CardinalityAuditService.cs | IDeviceRegistry | Constructor injection + AllDevices call in AuditCardinality() | WIRED | var devices = _registry.AllDevices; |
| CardinalityAuditService.cs | IOidMapService | Constructor injection + EntryCount call in AuditCardinality() | WIRED | var oidMapEntries = _oidMap.EntryCount; |
| appsettings.Development.json "Devices" section | DevicesOptions.Devices list | Configure<IConfiguration> delegate binding | WIRED | config.GetSection("Devices").Bind(opts.Devices) -- correct pattern for JSON array; standard Bind() silently fails |
| appsettings.Development.json "OidMap" section | OidMapOptions.Entries dictionary | Configure<IConfiguration> delegate binding | WIRED | config.GetSection("OidMap").Bind(opts.Entries) -- binds flat JSON object into Dictionary<string,string> |

---

### Requirements Coverage

| Requirement | Description | Status | Evidence |
|-------------|-------------|--------|----------|
| MAP-01 | Flat Dictionary<string,string> in appsettings under OidMap section | SATISFIED | OidMapOptions.Entries is Dictionary<string,string>; appsettings.Development.json has 5-entry "OidMap" section |
| MAP-02 | Maps OID string to metric_name | SATISFIED | Keys are OID strings, values are metric names (e.g., "hrProcessorLoad") |
| MAP-03 | OID in map -> metric_name; absent OID -> "Unknown" | SATISFIED | OidMapService.Resolve() returns Unknown constant; 3 unit tests verify including empty-map case |
| MAP-04 | Shared by traps and polls -- no device distinction | SATISFIED | Single global IOidMapService singleton; no per-device OID map |
| MAP-05 | Hot-reloadable without app restart | SATISFIED | IOptionsMonitor<OidMapOptions>.OnChange drives volatile FrozenDictionary atomic swap; 2 hot-reload unit tests pass |
| DEVC-01 | Per-device config: Name, IpAddress, MetricPolls | SATISFIED | DeviceOptions has Name, IpAddress, CommunityString? (nullable), List<MetricPollOptions> MetricPolls -- no DeviceType |
| DEVC-02 | Each MetricPoll: OID list + IntervalSeconds | SATISFIED | MetricPollOptions has List<string> Oids and int IntervalSeconds only -- no other fields |
| DEVC-03 | Device registry O(1) lookup by IP (traps) and by name (polls) | SATISFIED | Two FrozenDictionary fields; 10 unit tests covering IP (including IPv6-mapped), name (including case-insensitive), and miss cases |
| DEVC-04 | Quartz job identity: metric-poll-{deviceName}-{pollIndex} | SATISFIED | MetricPollInfo.JobKey() returns metric-poll-{deviceName}-{PollIndex}; unit test asserts "metric-poll-npb-core-01-0" |

---

### Anti-Patterns Found

None. No TODO, FIXME, placeholder, stub, or empty-return patterns detected in any Phase 2 production source file.

---

### Human Verification Required

None. All Phase 2 behaviors are verifiable structurally or via the 16 passing unit tests.

The OID map hot-reload in a live process (modifying appsettings.Development.json while the app runs) is a human-verifiable scenario, but it is not required for phase verification given that TestOptionsMonitor<T> exercises the OnChange callback path directly and synchronously in unit tests.

---

## Additional Observations

### AllDevices Allocation

DeviceRegistry.AllDevices is implemented as _byIp.Values.ToList().AsReadOnly(), which allocates a new list on each call. CardinalityAuditService calls it once at startup and the future Quartz scheduler will call it once at startup for job registration. These are startup-only call sites -- acceptable. Future consumers must not call AllDevices in a hot loop.

### OidMap Binding Pattern

The Configure<IConfiguration> delegate pattern for both DevicesOptions (JSON array) and OidMapOptions (flat JSON object) is necessary. The standard AddOptions<T>().Bind(GetSection("Devices")) silently fails to populate the Devices list on a DevicesOptions wrapper class -- it tries to map array index keys ("0", "1") as property names. This pitfall was discovered and corrected in 02-02.

### Build and Test Results

dotnet build src/SnmpCollector/SnmpCollector.csproj -c Release: Build succeeded. 0 Warning(s) 0 Error(s).

dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj -c Release: Passed\! Failed: 0, Passed: 16, Skipped: 0, Total: 16, Duration: 200ms.

---

_Verified: 2026-03-05_
_Verifier: Claude (gsd-verifier)_
