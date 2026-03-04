# Phase 2: Device Registry and OID Map - Research

**Researched:** 2026-03-05
**Domain:** .NET 9 Options pattern, IOptionsMonitor hot-reload, FrozenDictionary, device registry design
**Confidence:** HIGH

---

## Summary

This phase adds all the lookup structures that later phases query: a device registry (O(1) lookup by IP
and by name), an OID map (OID string -> metric_name), cardinality audit, and hot-reload of the OID map.
The reference implementation in `src/Simetra/` contains every pattern needed — `DeviceRegistry`,
`DevicesOptionsValidator`, and the `DevicesOptions`/`DeviceOptions`/`MetricPollOptions` class hierarchy
are all direct copy-and-adapt candidates. The SnmpCollector variant is simpler because it drops
`DeviceType` and the `IDeviceModule` system entirely.

The OID map hot-reload uses `IOptionsMonitor<T>` with `OnChange` callback. `Host.CreateApplicationBuilder`
already loads `appsettings.json` with `reloadOnChange: true` by default — no extra plumbing is needed.
The options class for OID map is a thin wrapper (`Dictionary<string, string>`) bound from the `OidMap`
section. The OnChange callback computes a diff (added/changed/removed) and logs a summary at Information
level, then replaces the live dictionary reference atomically using `volatile`.

For the device registry internal structure, `FrozenDictionary<TKey, TValue>` (available in-box since
.NET 8) is the correct choice for the read-heavy, write-once-at-startup pattern. The cardinality audit
is a computed check at startup: `devices x OIDs x 3 instruments x 2 sources`, logged as a Warning if
above a threshold.

**Primary recommendation:** Copy and adapt Simetra's `DeviceOptions`/`DevicesOptions`/`DevicesOptionsValidator`/
`DeviceRegistry`/`IDeviceRegistry` hierarchy into `SnmpCollector.*` namespaces, stripping `DeviceType`
and the module system. Add `CommunityString?` to `DeviceOptions` for optional per-device override. Use
`FrozenDictionary` for both registry lookups. Wire OID map hot-reload via `IOptionsMonitor<OidMapOptions>.OnChange`.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.Extensions.Options` | in-box (.NET 9) | `IOptions<T>`, `IOptionsMonitor<T>`, `ValidateOnStart` | Already in project via `Microsoft.Extensions.Hosting`; no additional package needed |
| `System.Collections.Frozen` | in-box (.NET 9) | `FrozenDictionary<TKey, TValue>` for O(1) read-only lookups | Available since .NET 8, zero-allocation lookups, purpose-built for read-heavy singletons |
| `Microsoft.Extensions.Options.DataAnnotations` | 9.0.0 | `ValidateDataAnnotations()` | Already added in Phase 1 (01-04 decision) |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Diagnostics.CodeAnalysis` | in-box | `[NotNullWhen]` for `TryGet` patterns | Use on `IDeviceRegistry.TryGetDevice` return parameters — matches Simetra's pattern exactly |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `FrozenDictionary` | `Dictionary<K,V>` (plain) | Plain Dictionary is thread-safe for read-only access after construction but lacks FrozenDictionary's read-optimized hash distribution. Acceptable, but FrozenDictionary is the better fit. |
| `FrozenDictionary` | `ConcurrentDictionary<K,V>` | ConcurrentDictionary adds per-operation locking overhead. It exists to support concurrent writes. Device registry is write-once (at startup), so ConcurrentDictionary is unnecessary overhead. |
| `IOptionsMonitor<T>.OnChange` | `IHostedService` polling file watcher | OnChange is the framework-native callback; no custom polling needed. `Host.CreateApplicationBuilder` already enables `reloadOnChange: true` for appsettings.json by default. |
| `volatile` field swap | `Interlocked.Exchange` | Both are correct for reference-type swaps. `volatile` field with assignment is simpler and sufficient — the CLR guarantees reference writes are atomic on 64-bit. |

### Installation

No new packages are required. All needed types are in the packages installed in Phase 1:
- `FrozenDictionary` is in `System.Collections.Immutable` which is transitively included via .NET 9 SDK
- `IOptionsMonitor<T>` is in `Microsoft.Extensions.Options` (in-box)

---

## Architecture Patterns

### Recommended Project Structure

Phase 2 adds the following to the existing `src/SnmpCollector/` layout:

```
src/SnmpCollector/
├── Configuration/
│   ├── DeviceOptions.cs              # NEW: Name, IpAddress, CommunityString?, MetricPolls[]
│   ├── DevicesOptions.cs             # NEW: SectionName="Devices", List<DeviceOptions>
│   ├── MetricPollOptions.cs          # NEW: OidList[], IntervalSeconds
│   ├── OidMapOptions.cs              # NEW: SectionName="OidMap", Dictionary<string,string> Entries
│   ├── SnmpListenerOptions.cs        # NEW: BindAddress, Port, CommunityString, Version
│   └── Validators/
│       ├── DevicesOptionsValidator.cs    # NEW: IValidateOptions<DevicesOptions>
│       └── SnmpListenerOptionsValidator.cs  # NEW: IValidateOptions<SnmpListenerOptions>
├── Pipeline/
│   ├── IDeviceRegistry.cs            # NEW: TryGetDevice(IPAddress), TryGetByName(string)
│   ├── DeviceRegistry.cs             # NEW: FrozenDictionary-backed singleton
│   ├── DeviceInfo.cs                 # NEW: sealed record (Name, IpAddress, CommunityString)
│   └── OidMapService.cs              # NEW: volatile FrozenDictionary + OnChange hot-reload
│       [or: IOidMapService.cs / OidMapService.cs split]
└── Extensions/
    └── ServiceCollectionExtensions.cs  # MODIFIED: AddSnmpConfiguration gains new options + registry registrations
```

### Pattern 1: Device Options Model (adapted from Simetra)

SnmpCollector's `DeviceOptions` is simpler than Simetra's — no `DeviceType`, no module matching.
The critical addition is optional `CommunityString?` for per-device override.

```csharp
// Configuration/DeviceOptions.cs
namespace SnmpCollector.Configuration;

/// <summary>
/// Configuration for a single monitored device.
/// Nested inside DevicesOptions — not a standalone IOptions registration.
/// </summary>
public sealed class DeviceOptions
{
    /// <summary>
    /// Human-readable device name (e.g., "npb-core-01"). Used as Quartz job identity component
    /// and as the 'agent' label value on emitted metrics.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the device for SNMP polling and trap source matching.
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-device SNMP community string override.
    /// When null or empty, falls back to SnmpListenerOptions.CommunityString.
    /// </summary>
    public string? CommunityString { get; set; }

    /// <summary>
    /// Metric polling configurations for this device. Each entry has its own OID list
    /// and IntervalSeconds (satisfies COLL-03 multiple poll groups per device).
    /// </summary>
    public List<MetricPollOptions> MetricPolls { get; set; } = [];
}
```

```csharp
// Configuration/MetricPollOptions.cs
namespace SnmpCollector.Configuration;

/// <summary>
/// One poll group for a device. Quartz job identity: metric-poll-{deviceName}-{pollIndex}
/// where pollIndex is the zero-based index in the device's MetricPolls list (DEVC-04).
/// </summary>
public sealed class MetricPollOptions
{
    /// <summary>
    /// OIDs to fetch in this poll group. Each OID resolves through the OidMap.
    /// </summary>
    public List<string> Oids { get; set; } = [];

    /// <summary>
    /// Polling interval in seconds. Must be > 0.
    /// </summary>
    public int IntervalSeconds { get; set; }
}
```

```csharp
// Configuration/DevicesOptions.cs
namespace SnmpCollector.Configuration;

/// <summary>
/// Wrapper for the Devices config array. Bound from "Devices" section.
/// </summary>
public sealed class DevicesOptions
{
    public const string SectionName = "Devices";

    public List<DeviceOptions> Devices { get; set; } = [];
}
```

**Key difference from Simetra:** No `DeviceType`, no `MetricName` on `MetricPollOptions`, no `OidEntryOptions`.
This project uses a flat OID list per poll group — the OID map resolves names, not the device config.

### Pattern 2: OID Map Options (new — no Simetra equivalent)

The OID map is a flat `Dictionary<string, string>` bound from appsettings. Binding works
because the configuration binder supports `Dictionary<string, string>` directly.

```csharp
// Configuration/OidMapOptions.cs
namespace SnmpCollector.Configuration;

/// <summary>
/// Flat OID-to-metric-name mapping. Bound from "OidMap" section.
/// Maps OID strings (e.g., "1.3.6.1.2.1.25.3.3.1.2") to camelCase metric names
/// (e.g., "hrProcessorLoad"). Used by OidResolutionBehavior in Phase 3.
/// Hot-reloadable via IOptionsMonitor (MAP-05).
/// </summary>
public sealed class OidMapOptions
{
    public const string SectionName = "OidMap";

    /// <summary>
    /// OID -> metric_name entries. The JSON section is a flat object:
    /// "OidMap": { "1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad" }
    /// An empty dictionary is valid (all OIDs resolve to "Unknown").
    /// </summary>
    public Dictionary<string, string> Entries { get; set; } = [];
}
```

**appsettings shape:**
```json
{
  "OidMap": {
    "1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad",
    "1.3.6.1.2.1.2.2.1.10": "ifInOctets",
    "1.3.6.1.2.1.1.3.0": "sysUpTime"
  }
}
```

**Binding note:** The existing `appsettings.json` already has `"OidMap": {}`. The `OidMapOptions.Entries`
property binds the entire section as a `Dictionary<string, string>` using standard options binding.
This works without any custom binder.

### Pattern 3: OID Map as Wrapper vs Direct Dictionary

Two valid approaches for what `OidMapOptions` holds:

**Option A (recommended):** Wrap the dictionary in a class with a property `Entries`:
- `services.AddOptions<OidMapOptions>().Bind(config.GetSection("OidMap"))` — this binds the ENTIRE
  OidMap section as a flat dictionary into `Entries` property. **This does NOT work** if the dictionary
  is bound directly from a sub-section because the binder maps section children to property children.

**Correct binding for flat dictionary at "OidMap" level:**
The dictionary entries ARE the direct children of the "OidMap" section. To bind them into a
`Dictionary<string, string>` property named `Entries`, the options class must bind the SECTION itself,
not a subsection. The pattern is:

```csharp
services.AddOptions<OidMapOptions>()
    .Configure(options =>
        configuration.GetSection(OidMapOptions.SectionName).Bind(options.Entries));
```

**OR Option B (simpler):** Use a dedicated wrapper type where the dictionary IS the entire section:

```csharp
// The simplest approach: bind the section directly into a class that IS the dictionary
public sealed class OidMapOptions
{
    public const string SectionName = "OidMap";
    // No Entries property — the class itself is the container
}
```

But this doesn't work with `AddOptions<T>().Bind()` because `Bind()` maps children of the section
to properties of the class.

**Resolution (HIGH confidence):** The correct approach for binding a flat JSON object section into a
`Dictionary<string, string>` property is:

```csharp
// In AddSnmpConfiguration:
services.AddOptions<OidMapOptions>()
    .Configure(opts =>
        configuration.GetSection(OidMapOptions.SectionName).Bind(opts.Entries));
```

OR use `Configure<IConfiguration>` overload:

```csharp
services.AddOptions<OidMapOptions>()
    .Configure<IConfiguration>((opts, config) =>
        config.GetSection(OidMapOptions.SectionName).Bind(opts.Entries));
```

This binds the flat key-value pairs under `"OidMap"` into `opts.Entries` dictionary. Tested pattern
in .NET configuration documentation.

### Pattern 4: Device Registry with FrozenDictionary (adapted from Simetra)

Simetra's `DeviceRegistry` uses plain `Dictionary` built at construction time — this is fine but
`FrozenDictionary` is a direct improvement for read-heavy access. The interface is identical.

```csharp
// Pipeline/IDeviceRegistry.cs
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace SnmpCollector.Pipeline;

/// <summary>
/// O(1) device lookup by IP address (trap path) and by name (poll path).
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>
    /// Attempts to find a device by sender IP address (normalized to IPv4).
    /// Used by the trap listener to identify the sending device.
    /// </summary>
    bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device);

    /// <summary>
    /// Attempts to find a device by configured name (case-insensitive).
    /// Used by poll jobs that receive device name from Quartz JobDataMap.
    /// </summary>
    bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device);

    /// <summary>
    /// All registered devices. Used for cardinality audit and Quartz job registration.
    /// </summary>
    IReadOnlyList<DeviceInfo> AllDevices { get; }
}
```

```csharp
// Pipeline/DeviceInfo.cs
namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable runtime representation of a monitored device.
/// CommunityString is the resolved value (per-device override or global default).
/// </summary>
public sealed record DeviceInfo(
    string Name,
    string IpAddress,
    string CommunityString,
    IReadOnlyList<MetricPollInfo> PollGroups);
```

```csharp
// Pipeline/MetricPollInfo.cs
namespace SnmpCollector.Pipeline;

/// <summary>
/// Runtime representation of one poll group. PollIndex is the zero-based position in
/// the device's MetricPolls list, used to compute Quartz job identity (DEVC-04).
/// </summary>
public sealed record MetricPollInfo(
    int PollIndex,
    IReadOnlyList<string> Oids,
    int IntervalSeconds)
{
    /// <summary>
    /// Quartz job identity for this poll group: "metric-poll-{deviceName}-{pollIndex}".
    /// </summary>
    public string JobKey(string deviceName) => $"metric-poll-{deviceName}-{PollIndex}";
}
```

```csharp
// Pipeline/DeviceRegistry.cs
using System.Collections.Frozen;
using System.Net;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton registry built once at startup from DevicesOptions.
/// Uses FrozenDictionary for read-optimized O(1) lookups.
/// IP lookup uses IPv4-normalized addresses to handle IPv4-mapped IPv6 addresses.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly FrozenDictionary<IPAddress, DeviceInfo> _byIp;
    private readonly FrozenDictionary<string, DeviceInfo> _byName;
    private readonly IReadOnlyList<DeviceInfo> _allDevices;

    public DeviceRegistry(
        IOptions<DevicesOptions> devicesOptions,
        IOptions<SnmpListenerOptions> snmpOptions)
    {
        var globalCommunity = snmpOptions.Value.CommunityString;
        var devices = devicesOptions.Value.Devices;

        var infos = devices.Select((d, index) => new DeviceInfo(
            d.Name,
            d.IpAddress,
            string.IsNullOrWhiteSpace(d.CommunityString) ? globalCommunity : d.CommunityString,
            d.MetricPolls.Select((p, i) => new MetricPollInfo(i, p.Oids.AsReadOnly(), p.IntervalSeconds))
                         .ToList()
                         .AsReadOnly()
        )).ToList();

        _allDevices = infos.AsReadOnly();

        _byIp = infos.ToFrozenDictionary(
            d => IPAddress.Parse(d.IpAddress).MapToIPv4());

        _byName = infos.ToFrozenDictionary(
            d => d.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device)
        => _byIp.TryGetValue(senderIp.MapToIPv4(), out device);

    public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
        => _byName.TryGetValue(deviceName, out device);

    public IReadOnlyList<DeviceInfo> AllDevices => _allDevices;
}
```

### Pattern 5: OID Map Service with Hot-Reload

The OID map service wraps the volatile lookup dictionary and wires `IOptionsMonitor.OnChange`.
The `IDisposable` returned by `OnChange` must be stored and disposed to prevent memory leaks.

```csharp
// Pipeline/IOidMapService.cs
namespace SnmpCollector.Pipeline;

/// <summary>
/// Provides OID-to-metric-name resolution. Hot-reloads when appsettings changes (MAP-05).
/// OIDs absent from the map resolve to "Unknown" (MAP-03).
/// </summary>
public interface IOidMapService
{
    /// <summary>
    /// Resolves an OID to its configured metric_name.
    /// Returns "Unknown" when the OID is not in the map (MAP-03).
    /// </summary>
    string Resolve(string oid);

    /// <summary>
    /// Current number of entries in the map. Used for cardinality audit.
    /// </summary>
    int EntryCount { get; }
}
```

```csharp
// Pipeline/OidMapService.cs
using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton OID map service. Resolves OID strings to metric names.
/// Hot-reloads on appsettings change via IOptionsMonitor (MAP-05).
/// Uses volatile field swap for lock-free read access during reload.
/// </summary>
public sealed class OidMapService : IOidMapService, IDisposable
{
    public const string Unknown = "Unknown";

    private volatile FrozenDictionary<string, string> _map;
    private readonly IDisposable? _changeToken;
    private readonly ILogger<OidMapService> _logger;

    public OidMapService(
        IOptionsMonitor<OidMapOptions> monitor,
        ILogger<OidMapService> logger)
    {
        _logger = logger;
        _map = monitor.CurrentValue.Entries.ToFrozenDictionary(StringComparer.Ordinal);

        // OnChange returns IDisposable — must be stored for cleanup (leak prevention)
        _changeToken = monitor.OnChange(OnOidMapChanged);
    }

    public string Resolve(string oid)
        => _map.TryGetValue(oid, out var name) ? name : Unknown;

    public int EntryCount => _map.Count;

    private void OnOidMapChanged(OidMapOptions newOptions, string? name)
    {
        var oldMap = _map;
        var newMap = newOptions.Entries.ToFrozenDictionary(StringComparer.Ordinal);

        // Compute diff for informational logging
        var added = newMap.Keys.Except(oldMap.Keys).Count();
        var removed = oldMap.Keys.Except(newMap.Keys).Count();
        var changed = newMap.Keys.Intersect(oldMap.Keys)
            .Count(k => oldMap[k] != newMap[k]);

        // Atomic reference swap — readers see either old or new, never partial
        _map = newMap;

        _logger.LogInformation(
            "OID map reloaded: {Added} added, {Changed} changed, {Removed} removed. Total: {Total} entries",
            added, changed, removed, newMap.Count);
    }

    public void Dispose() => _changeToken?.Dispose();
}
```

**Threading note on `volatile`:** The `volatile` keyword on `_map` ensures the CLR does not reorder
reads/writes around the assignment. A `FrozenDictionary` is immutable after creation, so readers that
have captured a reference continue to see a consistent snapshot. New readers after the swap see the
new map. This is the standard "immutable snapshot swap" pattern — no locks needed.

### Pattern 6: IOptionsMonitor OnChange — Callback Threading

The `OnChange` callback is invoked on a background thread (the file system watcher thread).
The callback must be fast and must not block. The `OidMapService.OnOidMapChanged` above satisfies
this: it does one LINQ computation, one volatile field assignment, and one log call.

`IOptionsMonitor<T>.OnChange(Action<T, string?> listener)` signature:
- First parameter: the new options value (already re-bound from config)
- Second parameter: the named options instance name (usually `null` for unnamed options)

The returned `IDisposable` registration must be disposed on service shutdown, otherwise the
registered delegate will be held in memory indefinitely (GC root through the options monitor).

### Pattern 7: Cardinality Audit at Startup

The audit runs once at startup (or after registry is built) and logs a warning if the estimate
exceeds the threshold. It does NOT block startup (warn-but-allow per CONTEXT.md decision).

```csharp
// In DeviceRegistry constructor, or in a startup IHostedService that runs after DI setup:
private static void AuditCardinality(
    IReadOnlyList<DeviceInfo> devices,
    IOidMapService oidMap,
    ILogger logger)
{
    const int InstrumentCount = 3;     // snmp_gauge, snmp_counter, snmp_info
    const int SourceCount = 2;         // poll, trap
    const int WarningThreshold = 10_000; // warn if estimated series exceeds this

    var totalOids = devices.Sum(d => d.PollGroups.Sum(p => p.Oids.Count));
    var estimate = devices.Count * (totalOids > 0 ? totalOids / Math.Max(devices.Count, 1) : 20)
                   * InstrumentCount * SourceCount;

    // Better estimate: unique OIDs across all devices x instruments x sources
    var uniqueOids = devices.SelectMany(d => d.PollGroups.SelectMany(p => p.Oids))
                            .Distinct()
                            .Count();
    var cardinality = devices.Count * uniqueOids * InstrumentCount * SourceCount;

    logger.LogInformation(
        "Cardinality estimate: {Devices} devices x {OIDs} unique OIDs x {Instruments} instruments " +
        "x {Sources} sources = ~{Total} series",
        devices.Count, uniqueOids, InstrumentCount, SourceCount, cardinality);

    if (cardinality > WarningThreshold)
    {
        logger.LogWarning(
            "Cardinality estimate {Total} exceeds threshold {Threshold}. " +
            "Consider reducing OID count or device count to avoid Prometheus performance issues.",
            cardinality, WarningThreshold);
    }
}
```

**Recommended threshold:** 10,000 series. For the target fleet (5-15 devices x 5-20 OIDs x 3 x 2 = 150-1800 series),
this threshold will never trigger in normal operation but catches runaway configs.

### Pattern 8: DevicesOptionsValidator (adapted from Simetra)

Simetra's validator is the direct reference. Adapt by removing `DeviceType` and `OidRole` checks,
adding validation that `MetricPolls[].Oids` is non-empty and `IntervalSeconds > 0`.

```csharp
// Configuration/Validators/DevicesOptionsValidator.cs
using Microsoft.Extensions.Options;

namespace SnmpCollector.Configuration.Validators;

public sealed class DevicesOptionsValidator : IValidateOptions<DevicesOptions>
{
    public ValidateOptionsResult Validate(string? name, DevicesOptions options)
    {
        var failures = new List<string>();

        for (var i = 0; i < options.Devices.Count; i++)
        {
            var device = options.Devices[i];
            ValidateDevice(device, i, failures);
        }

        ValidateNoDuplicates(options.Devices, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateDevice(DeviceOptions device, int index, List<string> failures)
    {
        var prefix = $"Devices[{index}]";

        if (string.IsNullOrWhiteSpace(device.Name))
            failures.Add($"{prefix}.Name is required");

        if (string.IsNullOrWhiteSpace(device.IpAddress))
            failures.Add($"{prefix}.IpAddress is required");
        else if (!System.Net.IPAddress.TryParse(device.IpAddress, out _))
            failures.Add($"{prefix}.IpAddress '{device.IpAddress}' is not a valid IP address");

        for (var j = 0; j < device.MetricPolls.Count; j++)
            ValidateMetricPoll(device.MetricPolls[j], prefix, j, failures);
    }

    private static void ValidateMetricPoll(MetricPollOptions poll, string devicePrefix, int index, List<string> failures)
    {
        var prefix = $"{devicePrefix}.MetricPolls[{index}]";

        if (poll.IntervalSeconds <= 0)
            failures.Add($"{prefix}.IntervalSeconds must be greater than 0");

        if (poll.Oids.Count == 0)
            failures.Add($"{prefix}.Oids must contain at least one OID");
    }

    private static void ValidateNoDuplicates(List<DeviceOptions> devices, List<string> failures)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < devices.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(devices[i].Name) && !seenNames.Add(devices[i].Name))
                failures.Add($"Devices[{i}].Name '{devices[i].Name}' is a duplicate");

            if (!string.IsNullOrWhiteSpace(devices[i].IpAddress) && !seenIps.Add(devices[i].IpAddress))
                failures.Add($"Devices[{i}].IpAddress '{devices[i].IpAddress}' is a duplicate");
        }
    }
}
```

### Pattern 9: SnmpListenerOptions (new — adapted from Simetra)

This section already exists as a placeholder in appsettings.json. The options class and validator
are direct copies of Simetra's equivalents with namespace adjustment. The key difference:
`CommunityString` is the GLOBAL default; per-device overrides live in `DeviceOptions.CommunityString?`.

```csharp
// Configuration/SnmpListenerOptions.cs
using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

public sealed class SnmpListenerOptions
{
    public const string SectionName = "SnmpListener";

    [Required]
    public required string BindAddress { get; set; } = "0.0.0.0";

    [Range(1, 65535)]
    public int Port { get; set; } = 162;

    /// <summary>
    /// Global default SNMP community string. Per-device overrides in DeviceOptions.CommunityString.
    /// </summary>
    [Required]
    public required string CommunityString { get; set; }

    [Required]
    [RegularExpression("^v2c$", ErrorMessage = "Only v2c is supported")]
    public required string Version { get; set; } = "v2c";
}
```

### Pattern 10: AddSnmpConfiguration Extension — Phase 2 Additions

Phase 2 augments `AddSnmpConfiguration` with new sections. The cardinality audit runs as a
singleton hosted service or inline in the startup path. DI registration order:
1. `DevicesOptions` with validator — fail-fast at startup
2. `OidMapOptions` — NOT ValidateOnStart (empty map is valid; hot-reload can populate it)
3. `SnmpListenerOptions` with validator — fail-fast at startup
4. `DeviceRegistry` singleton — takes `IOptions<DevicesOptions>` + `IOptions<SnmpListenerOptions>`
5. `OidMapService` singleton — takes `IOptionsMonitor<OidMapOptions>` (hot-reload capable)

```csharp
// In Extensions/ServiceCollectionExtensions.cs, AddSnmpConfiguration method:

// --- Phase 2: Device configuration ---
services.AddOptions<DevicesOptions>()
    .Configure<IConfiguration>((opts, config) =>
        config.GetSection(DevicesOptions.SectionName).Bind(opts.Devices))
    // OR: .Bind(configuration.GetSection(DevicesOptions.SectionName))  if binding works
    .ValidateOnStart();
services.AddSingleton<IValidateOptions<DevicesOptions>, DevicesOptionsValidator>();

// --- Phase 2: OID Map (no ValidateOnStart — empty is valid, hot-reload adds entries) ---
services.AddOptions<OidMapOptions>()
    .Configure<IConfiguration>((opts, config) =>
        config.GetSection(OidMapOptions.SectionName).Bind(opts.Entries));
// No ValidateDataAnnotations/ValidateOnStart — empty map is valid startup state

// --- Phase 2: SNMP Listener (needed before DeviceRegistry for community string fallback) ---
services.AddOptions<SnmpListenerOptions>()
    .Bind(configuration.GetSection(SnmpListenerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
services.AddSingleton<IValidateOptions<SnmpListenerOptions>, SnmpListenerOptionsValidator>();

// --- Phase 2: Registry and map services ---
services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
services.AddSingleton<IOidMapService, OidMapService>();
```

### Pattern 11: Binding DevicesOptions — Array vs Object Pattern

Simetra's `DevicesOptions` binds the `"Devices"` JSON array differently. The array in JSON is bound
by `Bind(configuration.GetSection("Devices"))` which maps the array to `DevicesOptions.Devices` list.
Standard `.Bind(section)` on a class with a `List<DeviceOptions> Devices` property DOES work when the
section is an array — the binder maps array elements to list elements.

```csharp
// Standard binding works for list properties:
services.AddOptions<DevicesOptions>()
    .Bind(configuration.GetSection(DevicesOptions.SectionName))
    .ValidateOnStart();
```

This binds `appsettings.json:Devices` (the JSON array) into `DevicesOptions.Devices` list.
Simetra uses a PostConfigure workaround for a different binding structure; SnmpCollector does not
need that workaround with the `DevicesOptions.Devices` wrapper property pattern.

### Anti-Patterns to Avoid

- **Disposing the `OnChange` registration inside the callback:** `_changeToken.Dispose()` should
  only be called in `OidMapService.Dispose()` (IDisposable). Calling it from inside the callback
  itself can cause deadlocks or skip future notifications.

- **Using `IOptionsSnapshot<OidMapOptions>` for hot-reload in a singleton:** `IOptionsSnapshot<T>`
  is Scoped — it cannot be injected into singletons. For singleton services that need live config
  updates, use `IOptionsMonitor<T>` exclusively.

- **Building FrozenDictionary from scratch in every Resolve() call:** FrozenDictionary is expensive
  to create (hash optimization at construction time). Create it once per reload, store as a field,
  use the field for all subsequent reads.

- **Using raw `IPAddress.ToString()` as a label value:** Raw IPs as label values create unbounded
  cardinality. The `agent` label uses device name (from DeviceConfig), not the IP address. This is
  a cardinality gate requirement from the CONTEXT.md.

- **Registering `IDeviceRegistry` as transient or scoped:** The registry is built once at startup
  and is immutable. It must be singleton. Same for `OidMapService`.

- **Forgetting that `OidMapOptions` hot-reload requires reloadOnChange on the config source:**
  `Host.CreateApplicationBuilder` sets `reloadOnChange: true` for appsettings.json by default.
  However, in Docker containers, `reloadOnChange` may not work because of how file mounts work.
  The `DOTNET_USE_POLLING_FILE_WATCHER=1` environment variable enables polling (every 4 seconds).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| File-change detection for config reload | Custom `FileSystemWatcher` | `IOptionsMonitor<T>.OnChange` | Framework already wires file watcher to options change notifications via `reloadOnChange`; no custom watcher needed |
| Thread-safe dictionary read during reload | `ReaderWriterLockSlim` | `volatile` field + immutable `FrozenDictionary` snapshot | Immutable snapshot swap is lock-free; readers capture the reference, use it, discard it — no contention |
| IP address normalization | Custom parser | `IPAddress.Parse(str).MapToIPv4()` | Handles IPv4-mapped IPv6 addresses (::ffff:10.0.0.1) transparently — same pattern as Simetra's DeviceRegistry |
| Duplicate detection in config | Manual scanning | `HashSet<string>` in `DevicesOptionsValidator.ValidateNoDuplicates` | Already in Simetra; copy directly |
| OID string lookup table | Custom trie or hash | `FrozenDictionary<string, string>` | FrozenDictionary is optimized for O(1) lookups on string keys; no custom structure needed |

**Key insight:** The .NET hosting and options infrastructure does all the heavy lifting (file watching,
change notification, options rebinding). The service layer only needs to consume the notification and
swap the immutable snapshot.

---

## Common Pitfalls

### Pitfall 1: OID Map Binding with Flat Dictionary

**What goes wrong:** Calling `.Bind(configuration.GetSection("OidMap"))` on an options class where the
target property is `Dictionary<string, string> Entries` — the binder maps section CHILDREN to
PROPERTY CHILDREN, not section to dictionary values.

**Why it happens:** The binder treats the `Entries` key as a subsection name, so it looks for
`OidMap:Entries` in config instead of `OidMap` itself.

**How to avoid:** Use `.Configure<IConfiguration>((opts, config) => config.GetSection("OidMap").Bind(opts.Entries))`
OR restructure appsettings so the map is under `OidMap:Entries` (breaking change to existing config).

The simplest correct approach given the existing `"OidMap": {}` shape in appsettings:

```csharp
services.AddOptions<OidMapOptions>()
    .Configure<IConfiguration>((opts, config) =>
        config.GetSection(OidMapOptions.SectionName).Bind(opts.Entries));
```

**Warning signs:** `opts.Entries` is always empty even when OidMap in appsettings has entries.

### Pitfall 2: OnChange Not Firing in Docker

**What goes wrong:** OID map hot-reload works on developer machines but not in Docker containers
or Kubernetes (mounted ConfigMaps).

**Why it happens:** The default `FileSystemWatcher` does not receive `Changed` events in environments
that use inotify substitutes or that mount files via container runtimes.

**How to avoid:** Set `DOTNET_USE_POLLING_FILE_WATCHER=1` environment variable in Docker/Kubernetes
deployments. This switches to polling every 4 seconds. Document this requirement for ops.

**Warning signs:** Config file changes have no effect; logs show no "OID map reloaded" messages after editing.

### Pitfall 3: IPAddress Equality with FrozenDictionary Key

**What goes wrong:** `IPAddress` implements `GetHashCode()` and `Equals()` correctly for IPv4/IPv6
comparison, BUT a device configured as `"10.0.1.1"` may arrive as an IPv4-mapped IPv6 address
`::ffff:10.0.1.1` in the trap listener. The dictionary key is normalized IPv4; the incoming address
is not.

**Why it happens:** SNMP trap reception can return IPv4-mapped IPv6 depending on socket binding.

**How to avoid:** Always call `.MapToIPv4()` on both the stored key (in `DeviceRegistry` constructor)
AND the incoming address (in `TryGetDevice`). Simetra does this correctly — copy the pattern.

**Warning signs:** Traps from known devices are logged as "unknown device" despite correct config.

### Pitfall 4: OnChange Memory Leak Without Dispose

**What goes wrong:** If `_changeToken?.Dispose()` is never called, the `OidMapService` instance is
permanently held in memory through the options monitor's listener list even after the service is
conceptually disposed. In practice, a singleton service lives for the entire application lifetime,
so this is a minor concern — but implementing `IDisposable` is the correct pattern.

**How to avoid:** Implement `IDisposable` on `OidMapService`, store the `IDisposable` token returned
by `OnChange`, and dispose it in `Dispose()`. Register `OidMapService` with `AddSingleton` — the DI
container will call `Dispose()` on singleton disposables during host shutdown.

**Warning signs:** Memory profiler shows OidMapService's delegate is rooted after service replacement tests.

### Pitfall 5: ValidateOnStart Interaction with OidMapOptions

**What goes wrong:** Adding `ValidateOnStart()` on `OidMapOptions` would block startup if the map
is empty. An empty map is a valid configuration (all OIDs will resolve to "Unknown", which is the
intended behavior per MAP-03).

**How to avoid:** Do NOT add `ValidateDataAnnotations()` or `ValidateOnStart()` to `OidMapOptions`
registration. The empty dictionary is a valid baseline. Only add startup validation if the map
is required to have at least N entries — which it is not per the CONTEXT.md decisions.

### Pitfall 6: FrozenDictionary Construction Cost During Reload

**What goes wrong:** `FrozenDictionary.ToFrozenDictionary()` is not O(1) — it performs hash analysis
to optimize the dictionary for reads. For a map with 5-20 entries, this is negligible (microseconds).
For a map with thousands of entries, it could briefly block the callback thread.

**Why it matters:** The `OnChange` callback runs on the file watcher thread. Blocking it delays
subsequent change notifications.

**How to avoid:** For the target fleet (5-20 OID entries), this is not a problem. If OID map grows to
hundreds of entries in the future, move FrozenDictionary construction to a `Task.Run` or use plain
`Dictionary` during reload. Document this as a known trade-off.

---

## Code Examples

### appsettings.json additions for Phase 2

```json
{
  "Devices": [
    {
      "Name": "npb-core-01",
      "IpAddress": "10.0.10.1",
      "MetricPolls": [
        {
          "Oids": ["1.3.6.1.4.1.2636.3.1.13.1.8"],
          "IntervalSeconds": 30
        },
        {
          "Oids": ["1.3.6.1.2.1.2.2.1.10", "1.3.6.1.2.1.2.2.1.16"],
          "IntervalSeconds": 300
        }
      ]
    },
    {
      "Name": "obp-edge-01",
      "IpAddress": "10.0.10.2",
      "CommunityString": "obp-secret",
      "MetricPolls": [
        {
          "Oids": ["1.3.6.1.2.1.25.3.3.1.2"],
          "IntervalSeconds": 60
        }
      ]
    }
  ],
  "OidMap": {
    "1.3.6.1.4.1.2636.3.1.13.1.8": "jnxOperatingCPU",
    "1.3.6.1.2.1.2.2.1.10": "ifInOctets",
    "1.3.6.1.2.1.2.2.1.16": "ifOutOctets",
    "1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad",
    "1.3.6.1.2.1.1.3.0": "sysUpTime"
  }
}
```

### Label Taxonomy (cardinality lock for Phase 3)

All three instruments (`snmp_gauge`, `snmp_counter`, `snmp_info`) share these labels:

| Label | Source | Example Values | Cardinality |
|-------|--------|----------------|-------------|
| `site_name` | `SiteOptions.Name` | `"site-nyc-01"` | 1 per deployment (bounded by config) |
| `metric_name` | OID map lookup | `"hrProcessorLoad"`, `"Unknown"` | bounded by OidMap size + 1 |
| `oid` | Raw OID string from trap/poll | `"1.3.6.1.2.1.25.3.3.1.2"` | bounded by OidMap size (only known OIDs tracked; Unknown OIDs use OID as label) |
| `agent` | `DeviceOptions.Name` | `"npb-core-01"` | bounded by device count in config |
| `source` | trap/poll path | `"poll"` or `"trap"` | 2 (constant) |

**Cardinality estimate for target fleet:**
- 15 devices x 20 OIDs x 3 instruments x 2 sources = **1,800 series maximum**
- Warning threshold: 10,000 series (will not trigger for target fleet)

**Cardinality gates (locked in Phase 2, enforced in Phase 3):**
- `agent` label = device `Name` from config (NOT raw IP address — IPs are unbounded on trap listener)
- `source` label = `"poll"` or `"trap"` (NOT poll interval — that would multiply cardinality by N)
- `metric_name` label = OID map value or `"Unknown"` (NOT raw OID strings on gauge/counter/info)
- `oid` label = raw OID string (acceptable — bounded by what devices actually send, not infinite)

### Quartz Job Identity Derivation (DEVC-04)

Job identity is derivable from device config at startup without any runtime state:

```csharp
// For device "npb-core-01" with 2 MetricPolls entries:
// PollIndex 0: "metric-poll-npb-core-01-0"
// PollIndex 1: "metric-poll-npb-core-01-1"

// In Phase 6 (scheduling), job identity derived from:
foreach (var device in registry.AllDevices)
{
    foreach (var pollGroup in device.PollGroups)
    {
        var jobKey = pollGroup.JobKey(device.Name);
        // jobKey = "metric-poll-npb-core-01-0"
        // This matches the pattern in DEVC-04
    }
}
```

### Unit Test Pattern for OID Resolution (no running host)

Success criterion #2 requires OID resolution to be testable without a running host.
`OidMapService` can be constructed directly in tests:

```csharp
// Unit test — no IHost needed
var options = new OidMapOptions
{
    Entries = new Dictionary<string, string>
    {
        ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
    }
};

// Use TestOptionsMonitor or mock IOptionsMonitor
var monitor = new TestOptionsMonitor<OidMapOptions>(options);
var service = new OidMapService(monitor, NullLogger<OidMapService>.Instance);

Assert.Equal("hrProcessorLoad", service.Resolve("1.3.6.1.2.1.25.3.3.1.2"));
Assert.Equal("Unknown", service.Resolve("1.3.6.1.999.999"));
```

A minimal `TestOptionsMonitor<T>` helper:

```csharp
// Used only in tests
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
```

---

## Simetra Adaptation Summary

| Simetra File | Action | Change Needed |
|---|---|---|
| `Configuration/DeviceOptions.cs` | Copy, simplify | Remove `DeviceType`. Add `CommunityString?`. Keep `Name`, `IpAddress`, `MetricPolls` list |
| `Configuration/DevicesOptions.cs` | Copy verbatim | Adjust namespace only |
| `Configuration/MetricPollOptions.cs` | Rewrite | Replace with `List<string> Oids` + `int IntervalSeconds`. Remove `MetricName`, `MetricType`, `OidEntryOptions`. This is a conceptual simplification |
| `Configuration/SnmpListenerOptions.cs` | Copy, simplify | Remove `Version` regex validation or keep it. `CommunityString` is the global default |
| `Configuration/Validators/DevicesOptionsValidator.cs` | Copy, adapt | Remove `DeviceType` validation, remove `OidRole`/`OidEntryOptions` validation. Add IP format validation. Keep duplicate detection |
| `Configuration/Validators/SnmpListenerOptionsValidator.cs` | Copy verbatim | Adjust namespace |
| `Pipeline/IDeviceRegistry.cs` | Copy, extend | Add `AllDevices` property for cardinality audit and Quartz job enumeration |
| `Pipeline/DeviceRegistry.cs` | Copy, refactor | Replace `Dictionary` with `FrozenDictionary`. Remove module/VirtualDevice logic entirely. Add community string resolution from global default |
| `Pipeline/DeviceInfo.cs` | Rewrite | Replace `DeviceType` and `TrapDefinitions` with `CommunityString` and `PollGroups` list |
| NEW `Pipeline/IOidMapService.cs` | Create new | No Simetra equivalent — flat global OID map not in Simetra |
| NEW `Pipeline/OidMapService.cs` | Create new | IOptionsMonitor hot-reload, volatile FrozenDictionary swap |
| NEW `Configuration/OidMapOptions.cs` | Create new | `Dictionary<string, string> Entries`, binds OidMap section |
| NEW `Pipeline/MetricPollInfo.cs` | Create new | Runtime representation of poll group with `JobKey(deviceName)` helper |

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Dictionary<K,V>` for read-only registries | `FrozenDictionary<K,V>` | .NET 8 (2023) | Read-optimized hash distribution; no change after creation; slightly faster lookup on hot paths |
| Manual `IChangeToken` polling for config reload | `IOptionsMonitor<T>.OnChange` | .NET Core 2.0+ | Framework handles file watching and callback dispatch; no custom polling code |
| `IHostedService` for config initialization side effects | Direct constructor init in singleton | Always valid | Singleton constructors run at first resolution; for startup ordering, `AddSingleton` factory delegates or `IHostedLifecycleService.StartingAsync` work |

**Deprecated/outdated:**
- `ConcurrentDictionary` for read-only singleton registries: Correct but suboptimal. `FrozenDictionary` is the right choice when the dictionary is populated once and then only read.
- Manual file watchers for config hot-reload: `IOptionsMonitor<T>` handles this since .NET Core 2.0.

---

## Open Questions

1. **DevicesOptions binding — list vs section binding**
   - What we know: `Bind(configuration.GetSection("Devices"))` binds a JSON array into a `List<DeviceOptions>` property on a wrapper class. This is standard.
   - What's unclear: Whether to use `.Bind(section)` on `DevicesOptions` (which has a `List<DeviceOptions> Devices` property) or the PostConfigure workaround Simetra uses.
   - Recommendation: Try `services.AddOptions<DevicesOptions>().Bind(configuration.GetSection("Devices"))` first. The `Devices` property name must match the section name or binding fails. If binding the section named `"Devices"` into a wrapper object with a `Devices` property doesn't work, use the `.Configure<IConfiguration>` approach. Verify with a unit test before committing.

2. **OidMapOptions binding — flat dictionary**
   - What we know: Binding `"OidMap"` section (flat JSON object) into `Dictionary<string, string> Entries` requires calling `section.Bind(opts.Entries)` not `section.Bind(opts)`.
   - What's unclear: Whether the natural `.Bind(section)` (which maps section children to property children) works if the property is named `Entries` and the JSON section is flat keys.
   - Recommendation: Use the explicit `.Configure<IConfiguration>` pattern shown above. This is verified correct.

3. **Cardinality audit trigger — constructor vs hosted service**
   - What we know: The audit needs both `IDeviceRegistry` and `IOidMapService` to be built.
   - What's unclear: Whether to audit in `DeviceRegistry` constructor (can't access OidMapService there without circular dep) or in a startup `IHostedLifecycleService`.
   - Recommendation: Run the audit in a thin `CardinalityAuditService : IHostedLifecycleService` that runs in `StartingAsync`. It takes `IDeviceRegistry` and `IOidMapService` as dependencies, logs the estimate, and exits. This separates concerns cleanly.

---

## Sources

### Primary (HIGH confidence)

- `src/Simetra/Pipeline/IDeviceRegistry.cs` — Interface contract with `[NotNullWhen]` pattern
- `src/Simetra/Pipeline/DeviceRegistry.cs` — Reference implementation: constructor init, IP normalization, dual-keyed lookup
- `src/Simetra/Pipeline/DeviceInfo.cs` — Sealed record pattern for immutable device info
- `src/Simetra/Configuration/DeviceOptions.cs` — Device config class shape (Name, IpAddress, MetricPolls)
- `src/Simetra/Configuration/DevicesOptions.cs` — Wrapper class with SectionName constant
- `src/Simetra/Configuration/MetricPollOptions.cs` — Poll group options shape (adapted)
- `src/Simetra/Configuration/SnmpListenerOptions.cs` — Listener options with community string
- `src/Simetra/Configuration/Validators/DevicesOptionsValidator.cs` — Full validator with nested validation and duplicate detection
- `src/Simetra/Configuration/Validators/SnmpListenerOptionsValidator.cs` — Listener validator pattern
- `src/SnmpCollector/appsettings.json` — Confirms `"OidMap": {}` placeholder already exists
- Official .NET docs: `IOptionsMonitor<T>.OnChange` signature (Action<TOptions, string?> -> IDisposable)
- Official .NET docs: `FrozenDictionary<K,V>` — in-box since .NET 8, `ToFrozenDictionary()` LINQ extension
- Official .NET docs: `Host.CreateApplicationBuilder` loads appsettings.json with reloadOnChange by default
- Official .NET docs: `IOptionsMonitor` is Singleton-safe; `IOptionsSnapshot` is Scoped (cannot inject into Singleton)

### Secondary (MEDIUM confidence)

- Official .NET docs on configuration binding to `Dictionary<string, string>` — binding section children to property children; confirmed `Bind()` behavior for dictionaries
- Official .NET docs on `DOTNET_USE_POLLING_FILE_WATCHER` for Docker environments

### Tertiary (LOW confidence)

- Pattern for `volatile` field swap on `FrozenDictionary` for lock-free reload — standard pattern, not from a specific official source but consistent with CLR memory model documentation

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all types are in-box .NET 9, verified against official API docs
- Architecture patterns: HIGH — Simetra reference implementation inspected directly; options monitor API verified against official docs
- OID map binding: MEDIUM — `Dictionary<string, string>` binding with `.Configure` approach is the correct pattern, but the exact wiring should be verified with a quick build test before committing to a plan task
- Pitfalls: HIGH — IP normalization pitfall directly observed in Simetra's codebase; IOptionsSnapshot-in-singleton pitfall is documented in official docs; Docker file watcher limitation is documented

**Research date:** 2026-03-05
**Valid until:** 2026-04-05 (stable .NET APIs; FrozenDictionary introduced .NET 8 so no version risk)
