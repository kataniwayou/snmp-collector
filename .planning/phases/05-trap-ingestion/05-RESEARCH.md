# Phase 5: Trap Ingestion - Research

**Researched:** 2026-03-05
**Domain:** SharpSnmpLib 12.5.7 trap reception, System.Threading.Channels BoundedChannel, MediatR ISender.Send, BackgroundService patterns
**Confidence:** HIGH

---

## Summary

Phase 5 adds SNMP v2c trap reception to the SnmpCollector project. The goal is a BackgroundService (`SnmpTrapListenerService`) that receives UDP datagrams on port 162, authenticates via community string, routes each varbind as a separate `SnmpOidReceived` through a per-device `BoundedChannel`, and a second BackgroundService (`ChannelConsumerService`) that reads from those channels and calls `ISender.Send()` into the MediatR pipeline.

The standard approach is: raw `UdpClient` receive loop + `MessageFactory.ParseMessages()` for parsing (SharpSnmpLib already installed) + `System.Threading.Channels.Channel.CreateBounded()` with `DropOldest` for backpressure. The `DeviceRegistry` already resolves community strings per-device (built in Phase 2, `DeviceInfo.CommunityString`), and the `ValidationBehavior` already handles unknown-device rejection in the MediatR pipeline. The three new counters (`snmp.trap.auth_failed`, `snmp.trap.unknown_device`, `snmp.trap.dropped`) must be added to `PipelineMetricService`.

The critical architectural constraint is: the listener must NEVER call `ISender.Send()` directly — all varbinds flow through the per-device channel first. The `ChannelConsumerService` is the only caller of `ISender.Send()`. This is verifiable by code structure: the listener only calls `channel.Writer.TryWrite()`.

**Primary recommendation:** Use raw `UdpClient` receive loop + `MessageFactory.ParseMessages()` (not SharpSnmpLib's built-in listener/engine), one `BoundedChannel<VarbindEnvelope>` per device (capacity 1,000, DropOldest), one consumer Task per device via `ReadAllAsync()`. Auth check BEFORE device lookup (security-first ordering). Periodic drop warning every 100 drops.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Lextm.SharpSnmpLib | 12.5.7 | `MessageFactory.ParseMessages()`, `TrapV2Message`, `ISnmpData`, community string extraction | Already in SnmpCollector.csproj; already used in Phase 2-4 |
| System.Threading.Channels | BCL (.NET 9) | `BoundedChannel<T>` with `DropOldest`, `ReadAllAsync()` | Part of .NET BCL, no NuGet needed |
| Microsoft.Extensions.Hosting | 9.0.0 | `BackgroundService` base class | Already in project |
| MediatR | 12.5.0 | `ISender.Send()` for pipeline dispatch | Already in project, locked version |

### No New Packages Required

All required packages are already in `SnmpCollector.csproj`. Phase 5 adds no new NuGet references.

---

## Architecture Patterns

### Recommended Project Structure (new files Phase 5 adds)

```
src/SnmpCollector/
├── Services/
│   ├── SnmpTrapListenerService.cs     # BackgroundService: UDP receive + auth + channel write
│   └── ChannelConsumerService.cs      # BackgroundService: channel read + ISender.Send per varbind
├── Pipeline/
│   ├── IDeviceChannelManager.cs       # Interface: GetWriter, GetReader, DeviceNames, CompleteAll
│   ├── DeviceChannelManager.cs        # Singleton: creates BoundedChannel<VarbindEnvelope> per device
│   └── VarbindEnvelope.cs             # Struct/record: OID + ISnmpData + AgentIp + source="trap"
└── Telemetry/
    └── PipelineMetricService.cs       # MODIFIED: add 3 new counters (auth_failed, unknown_device, dropped)
```

### Pattern 1: UdpClient Receive Loop (BackgroundService)

**What:** Raw `UdpClient` bound to the configured endpoint in `ExecuteAsync`. Receive loop calls `ReceiveAsync(stoppingToken)`. Each datagram is processed synchronously or via lightweight `Task`.

**Key reference:** The Simetra project's `SnmpListenerService.cs` is the authoritative reference. It uses exactly this pattern and was read directly from the codebase (HIGH confidence).

```csharp
// Source: src/Simetra/Services/SnmpListenerService.cs (read directly)
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var endpoint = new IPEndPoint(
        IPAddress.Parse(_listenerOptions.BindAddress),
        _listenerOptions.Port);

    using var udpClient = new UdpClient(endpoint);
    var userRegistry = new UserRegistry();  // required by MessageFactory; empty for v2c

    _logger.LogInformation(
        "Trap listener bound to UDP {Port}, monitoring {N} devices",
        _listenerOptions.Port,
        _deviceRegistry.AllDevices.Count);

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            var result = await udpClient.ReceiveAsync(stoppingToken);
            ProcessDatagram(result);  // sync — no await needed for channel TryWrite
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            break;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("Socket error receiving SNMP trap: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Malformed SNMP packet from {Ip}: {Message}",
                "unknown", ex.Message);
        }
    }
}
```

**Why `UserRegistry` is needed:** `MessageFactory.ParseMessages()` requires a `UserRegistry` parameter even for v2c messages (which don't use USM). An empty `new UserRegistry()` is correct for v2c — it satisfies the API without enabling v3 auth.

### Pattern 2: MessageFactory.ParseMessages + TrapV2Message

**What:** `MessageFactory.ParseMessages(buffer, index, length, userRegistry)` returns `IList<ISnmpMessage>`. Each message is checked with `is TrapV2Message`. Non-trap messages are silently ignored.

```csharp
// Source: src/Simetra/Services/SnmpListenerService.cs (read directly) — HIGH confidence
var messages = MessageFactory.ParseMessages(
    result.Buffer, 0, result.Buffer.Length, userRegistry);

foreach (var message in messages)
{
    if (message is not TrapV2Message trapV2)
        continue;  // ignore non-trap messages (polls, INFORM, etc.)

    // Community string is accessed via extension method
    var community = trapV2.Community().ToString();
    // ...
}
```

**Community string access:** `trapV2.Community()` is a SharpSnmpLib extension method that returns an `OctetString`. `.ToString()` gives the ASCII community string value. This is the correct API — verified from Simetra reference implementation.

**Variables access:** `trapV2.Variables()` returns `IList<Variable>`. Each `Variable` has:
- `.Id` — the OID as an `ObjectIdentifier`
- `.Data` — the value as `ISnmpData`

The `Variable.Id.ToString()` gives the dotted-decimal OID string (e.g., `"1.3.6.1.2.1.1.1.0"`).

```csharp
// Source: src/Simetra/Services/SnmpListenerService.cs (read directly)
foreach (var variable in trapV2.Variables())
{
    var oid = variable.Id.ToString();
    var data = variable.Data;
    var typeCode = data.TypeCode;
    // one SnmpOidReceived per varbind
}
```

### Pattern 3: Community String Auth (Security-First Ordering)

**Decision (Claude's Discretion — must decide):** Auth BEFORE device registry lookup.

**Rationale:** Failing fast on auth prevents any computation on unauthenticated packets. A malicious device flood with wrong community strings would never reach registry lookup (saves a dictionary lookup per packet). The security-first ordering is standard practice and matches RFC 2576 intent.

**Implementation:**
```csharp
// 1. Parse message, check it's TrapV2Message
// 2. Extract source IP from UDP header
// 3. Look up device by IP to get expected community string
// 4. Compare community strings (case-sensitive, RFC-compliant)

// CHECK ORDERING for this phase:
// Step A: Look up device by IP first (to get per-device community string)
// Step B: Compare community (device.CommunityString, which is already resolved in DeviceRegistry)
// If device not found → log Warning + snmp.trap.unknown_device + drop
// If community mismatch → log Warning + snmp.trap.auth_failed + drop

// Note: DeviceRegistry already resolves community at startup:
// DeviceInfo.CommunityString = per-device override OR global fallback
// This means device lookup must happen BEFORE community comparison
// because the expected community string lives on DeviceInfo.
```

**Critical insight about check ordering:** Because the expected community string is stored on `DeviceInfo` (resolved at startup by `DeviceRegistry`), the actual order must be:
1. Device lookup by source IP
2. If not found → `snmp.trap.unknown_device` + Warning + drop
3. If found → compare `device.CommunityString` (the already-resolved value) with received community
4. If mismatch → `snmp.trap.auth_failed` + Warning (include source IP and received community) + drop
5. If match → route varbinds to channel

This order is correct and efficient: the device lookup is O(1) via `FrozenDictionary`, and the auth check is a single string comparison.

### Pattern 4: VarbindEnvelope — Channel Message Type

**What:** A lightweight value type carrying the minimum needed to construct `SnmpOidReceived` in the consumer.

```csharp
// New file: src/SnmpCollector/Pipeline/VarbindEnvelope.cs
// Source: design based on SnmpOidReceived shape (read directly)
namespace SnmpCollector.Pipeline;

/// <summary>
/// Lightweight message written to per-device BoundedChannel by SnmpTrapListenerService.
/// Carries one varbind from a received trap, plus the sender address for SnmpOidReceived construction.
/// </summary>
public sealed record VarbindEnvelope(
    string Oid,
    ISnmpData Value,
    SnmpType TypeCode,
    IPAddress AgentIp,
    string DeviceName   // pre-resolved at listener time (known after device lookup)
);
```

**Why include DeviceName:** The listener already did the device registry lookup to authenticate. Passing `DeviceName` avoids a second lookup in the consumer. The consumer sets `DeviceName` on `SnmpOidReceived` before calling `ISender.Send()` — which means `ValidationBehavior`'s `msg.DeviceName is null` check is bypassed (correct, because it's already resolved).

### Pattern 5: BoundedChannel with DropOldest + Drop Callback

**What:** One `Channel<VarbindEnvelope>` per device, created at startup by `DeviceChannelManager`. Capacity 1,000 (per CONTEXT.md decision). The `itemDropped` callback fires synchronously on the writer thread when the channel is full and an item is dropped.

```csharp
// Source: Microsoft Docs channels.md (read directly — HIGH confidence)
// Source: src/Simetra/Pipeline/DeviceChannelManager.cs (read directly — HIGH confidence)
var options = new BoundedChannelOptions(1_000)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleWriter = false,   // multiple trap packets may arrive concurrently
    SingleReader = true,    // one consumer Task per device
    AllowSynchronousContinuations = false
};

var deviceName = d.Name;  // capture for closure
var channel = Channel.CreateBounded(options, (VarbindEnvelope dropped) =>
{
    // itemDropped callback — fires on writer thread, keep it fast
    // Periodic Warning log (every 100 drops) + counter increment
    var dropCount = Interlocked.Increment(ref _dropCounters[deviceName]);
    _pipelineMetrics.IncrementTrapDropped(deviceName);

    if (dropCount % 100 == 0)
    {
        _logger.LogWarning(
            "Trap channel for {DeviceName} has dropped {Count} varbinds (capacity: 1000)",
            deviceName, dropCount);
    }
});
```

**Periodic drop warning interval:** Every 100 drops. Rationale: trap storms at 1,000+ traps/second would produce 10+ warnings/second at every-drop logging. 100-drop intervals give one warning per ~100ms at 1,000 traps/sec — visible but not overwhelming.

**`AllowSynchronousContinuations = false`:** Required because the UDP receive loop must not block on consumer continuations. Setting `false` ensures consumer processing is posted to the thread pool, not run inline on the listener thread.

### Pattern 6: ChannelConsumerService — One Task Per Device

**What:** A BackgroundService that spawns one consumer `Task` per device at startup using `Task.WhenAll`.

```csharp
// Source: src/Simetra/Services/ChannelConsumerService.cs (read directly — HIGH confidence)
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var tasks = _channelManager.DeviceNames
        .Select(name => ConsumeDeviceAsync(name, stoppingToken))
        .ToArray();

    await Task.WhenAll(tasks);
}

private async Task ConsumeDeviceAsync(string deviceName, CancellationToken ct)
{
    var reader = _channelManager.GetReader(deviceName);

    await foreach (var envelope in reader.ReadAllAsync(ct))
    {
        try
        {
            var msg = new SnmpOidReceived
            {
                Oid        = envelope.Oid,
                AgentIp    = envelope.AgentIp,
                DeviceName = envelope.DeviceName,  // pre-resolved
                Value      = envelope.Value,
                Source     = SnmpSource.Trap,       // always "trap" for trap-originated events
                TypeCode   = envelope.TypeCode,
            };

            _pipelineMetrics.IncrementTrapReceived();   // snmp.trap.received (PMET-06, existing)
            await _sender.Send(msg, ct);                // ISender.Send — NOT IPublisher.Publish
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing varbind for {DeviceName}", deviceName);
        }
    }
}
```

**Why `ReadAllAsync()` ends naturally:** When `DeviceChannelManager.CompleteAll()` is called during graceful shutdown (marks all writers complete), `ReadAllAsync()` will drain remaining items then complete its `IAsyncEnumerable`. The consumer Tasks finish naturally. This is the correct graceful drain pattern.

**`ISender` vs `ISender.Send` note:** `ISender` is already registered by `AddMediatR()`. Inject `ISender` (not `IMediator`) to express intent — sending a request, not publishing a notification. SnmpOidReceived implements `IRequest<Unit>`, not `INotification`.

### Pattern 7: IDeviceChannelManager Interface + DeviceChannelManager Singleton

```csharp
// New file: src/SnmpCollector/Pipeline/IDeviceChannelManager.cs
public interface IDeviceChannelManager
{
    ChannelWriter<VarbindEnvelope> GetWriter(string deviceName);
    ChannelReader<VarbindEnvelope> GetReader(string deviceName);
    IReadOnlyCollection<string> DeviceNames { get; }
    void CompleteAll();                           // called by graceful shutdown
}
```

**DeviceChannelManager initialization:** Takes `IOptions<DevicesOptions>` to create channels for each configured device. Uses `IDeviceRegistry.AllDevices` alternatively. The `_dropCounters` dictionary (for periodic warning) can be a `ConcurrentDictionary<string, long>` or a per-device `long` field with `Interlocked.Increment`.

### Pattern 8: New PipelineMetricService Counters

Three new counters must be added to `PipelineMetricService` (in `src/SnmpCollector/Telemetry/PipelineMetricService.cs`):

```csharp
// ADDED to existing PipelineMetricService constructor:
private readonly Counter<long> _trapAuthFailed;
private readonly Counter<long> _trapUnknownDevice;
private readonly Counter<long> _trapDropped;

// In constructor:
_trapAuthFailed     = _meter.CreateCounter<long>("snmp.trap.auth_failed");
_trapUnknownDevice  = _meter.CreateCounter<long>("snmp.trap.unknown_device");
_trapDropped        = _meter.CreateCounter<long>("snmp.trap.dropped");

// New public methods:
public void IncrementTrapAuthFailed()
    => _trapAuthFailed.Add(1, new TagList { { "site_name", _siteName } });

public void IncrementTrapUnknownDevice()
    => _trapUnknownDevice.Add(1, new TagList { { "site_name", _siteName } });

public void IncrementTrapDropped(string deviceName)
    => _trapDropped.Add(1, new TagList
        { { "site_name", _siteName }, { "device_name", deviceName } });
```

**Label note:** `snmp.trap.dropped` includes `device_name` because it must be per-device (per CONTEXT.md). `snmp.trap.auth_failed` and `snmp.trap.unknown_device` use `site_name` only — they don't have a device yet (either unknown or unauthenticated).

### Pattern 9: Startup Log + First-Contact Tracking

**Startup log:** In `ExecuteAsync` after binding the socket:
```csharp
_logger.LogInformation(
    "Trap listener bound to UDP {Port}, monitoring {N} devices",
    _listenerOptions.Port,
    _deviceRegistry.AllDevices.Count);
```

**First-contact log:** Track per-device "first trap seen" with a `HashSet<string>` or `ConcurrentDictionary<string, bool>` in the listener service:
```csharp
private readonly ConcurrentDictionary<string, byte> _seenDevices = new();

// In ProcessDatagram, after device lookup succeeds:
if (_seenDevices.TryAdd(device.Name, 0))
{
    _logger.LogInformation(
        "First trap received from {DeviceName} ({Ip})",
        device.Name,
        senderIp);
}
```

### Pattern 10: DI Registration (AddSnmpPipeline modification)

Phase 5 extends `AddSnmpPipeline()` in `ServiceCollectionExtensions.cs`:

```csharp
// Added to AddSnmpPipeline():
services.AddSingleton<IDeviceChannelManager, DeviceChannelManager>();
services.AddHostedService<SnmpTrapListenerService>();
services.AddHostedService<ChannelConsumerService>();
```

**Registration order matters:** `DeviceChannelManager` must be registered before the hosted services that depend on it. `SnmpTrapListenerService` starts before `ChannelConsumerService` (registration order = start order for hosted services). Both are after `DeviceRegistry` (Phase 2).

**Graceful shutdown note:** `ChannelConsumerService` should be registered AFTER `SnmpTrapListenerService` in DI. The generic host stops services in REVERSE registration order — so `ChannelConsumerService` stops first (safe: listener already stopped, channels will drain), then `SnmpTrapListenerService` stops. For complete drain, call `DeviceChannelManager.CompleteAll()` during shutdown (which can be triggered via a dedicated `IHostApplicationLifetime.ApplicationStopping` handler or a graceful shutdown service).

### Anti-Patterns to Avoid

- **Calling `ISender.Send()` from `SnmpTrapListenerService`:** Violates the requirement that the listener NEVER publishes directly to MediatR. The channel is the mandatory intermediary. Verifiable: the listener class must not reference `ISender`.
- **Using `IPublisher.Publish()`:** `SnmpOidReceived` implements `IRequest<Unit>`, not `INotification`. `IPublisher.Publish()` only works for `INotification` types — the behaviors (Logging, Exception, Validation, OidResolution) would NOT run.
- **Creating a new `UserRegistry` per datagram:** `UserRegistry` is thread-safe and stateless for v2c. Create once per `ExecuteAsync` invocation, reuse across all datagrams.
- **Using `WriteAsync()` from the listener:** `WriteAsync()` on a `DropOldest` bounded channel will NOT block (writes complete immediately via dropping). But `TryWrite()` is the correct choice for the listener — it is synchronous, returns bool, and never waits. The `itemDropped` callback fires synchronously.
- **Logging on every dropped varbind:** With DropOldest under a trap storm, drops can reach thousands per second. Log periodically (every N drops), not on every drop.
- **Not setting `SnmpOidReceived.Source = SnmpSource.Trap`:** The pipeline label taxonomy includes `source` (poll/trap). Missing this results in all trap-originated OTel measurements being attributed to `source=""` or `"poll"`.
- **Using `channel.Writer.WriteAsync()` with DropOldest:** With `DropOldest` full mode, `WriteAsync` returns immediately (the old item is dropped to make room). Use `TryWrite()` to get the bool return that lets you track whether an item was written.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Channel backpressure with DropOldest | Custom ring buffer or Queue with lock | `Channel.CreateBounded(..., DropOldest)` | itemDropped callback, thread-safe, ReadAllAsync integration |
| SNMP message parsing | Manual BER/ASN.1 decoder | `MessageFactory.ParseMessages()` | Already in project; handles all PDU types, error cases |
| Community string resolution | Separate lookup table | `DeviceInfo.CommunityString` (already in DeviceRegistry) | DeviceRegistry resolves per-device vs. global at startup in Phase 2 |
| Per-device consumer tasks | Thread pool + manual coordination | `ReadAllAsync()` + `Task.WhenAll` | AsyncEnumerable completes naturally when writer is marked complete |
| Unknown-device metric in pipeline | New behavior | Existing `ValidationBehavior` handles it | ValidationBehavior already rejects `DeviceName == null` + IP not in registry |

**Key insight:** The `DeviceRegistry` (Phase 2) already resolves community strings. Phase 5 only needs to look up `DeviceInfo.CommunityString` — no separate auth table needed.

---

## Common Pitfalls

### Pitfall 1: `SnmpOidReceived` is `IRequest<Unit>`, Not `INotification`

**What goes wrong:** Using `IPublisher.Publish()` instead of `ISender.Send()`. The behaviors (Logging, Exception, Validation, OidResolution) only fire for `IRequest<T>` dispatched via `ISender.Send()`. With `IPublisher.Publish()`, all behaviors are bypassed — OID validation, device lookup, metric recording all fail silently.

**Why it happens:** Confusion between notification/request terminology. Phase 3 locked `SnmpOidReceived : IRequest<Unit>` specifically to enable the behavior pipeline.

**How to avoid:** Inject `ISender` (not `IPublisher`, not `IMediator`) into `ChannelConsumerService`. The type system enforces `IRequest<Unit>` vs `INotification` at compile time when using the correct interface.

**Warning signs:** No Debug logs from `LoggingBehavior` appear, but traps are arriving. No `snmp.event.published` increments.

### Pitfall 2: Auth Check Ordering — Community String Lives on DeviceInfo

**What goes wrong:** Trying to check community string BEFORE device lookup, but the expected community string is on `DeviceInfo` — so you can't check auth without first looking up the device.

**Why it happens:** The CONTEXT.md says "community string auth happens before or after device registry lookup" is Claude's discretion. It looks like auth-first is possible but it's not — because the per-device community string is only available after the device lookup.

**How to avoid:** Always do device lookup first, then check `device.CommunityString`. A trap from an unknown IP gets `snmp.trap.unknown_device`. A trap from a known IP with wrong community gets `snmp.trap.auth_failed`. These are two different failure modes with different counters.

### Pitfall 3: `TryWrite()` vs `WriteAsync()` with DropOldest

**What goes wrong:** Using `WriteAsync()` on a `DropOldest` channel and expecting it to block when full. With `DropOldest`, `WriteAsync()` completes immediately because the channel makes room by dropping the oldest item. This is actually fine for the listener, but `TryWrite()` is preferred because:
- It returns `bool` indicating whether the write succeeded (useful for the `itemDropped` callback tracking)
- It is synchronous — no await needed
- It is explicitly non-blocking

**How to avoid:** Use `TryWrite()` in the listener. The `itemDropped` callback is the mechanism for tracking drops.

**Note from official docs:** With `DropOldest`, the `itemDropped` callback is invoked on the writer thread (synchronously). Keep the callback fast: increment a counter and conditionally log. Do not await or call blocking code.

### Pitfall 4: Variable.Id OID String Format

**What goes wrong:** `variable.Id.ToString()` includes the leading `"."` in some versions of SharpSnmpLib, producing `".1.3.6.1.2.1.1.1.0"` instead of `"1.3.6.1.2.1.1.1.0"`. The OID regex in `ValidationBehavior` (`^\d+(\.\d+){1,}$`) rejects strings with a leading dot.

**How to avoid:** Verify the format of `variable.Id.ToString()` against a test trap. If a leading dot appears, strip it: `oid.TrimStart('.')`. From the Simetra reference codebase, there is no `.TrimStart('.')` in the existing code, suggesting SharpSnmpLib 12.5.7 returns the correct format without a leading dot. Confidence: MEDIUM (from code pattern, not official docs). Write a unit test to confirm.

### Pitfall 5: Missing `SnmpSource.Trap` on Constructed `SnmpOidReceived`

**What goes wrong:** The consumer constructs `SnmpOidReceived` with `Source = SnmpSource.Poll` (default or copy from somewhere else). The `source` label on OTel metrics will show `"poll"` for trap-originated data.

**How to avoid:** Always set `Source = SnmpSource.Trap` explicitly in `ChannelConsumerService.ConsumeDeviceAsync()`. The CONTEXT.md specifically states "SnmpOidReceived.Source should be 'trap' for trap-originated events."

### Pitfall 6: BoundedChannel Capacity Mismatch with ChannelsOptions

**What goes wrong:** The existing `ChannelsOptions.BoundedCapacity` defaults to 100, but CONTEXT.md locks the trap channel capacity at 1,000.

**How to avoid:** Either update `ChannelsOptions.BoundedCapacity` default to 1,000, or use a hardcoded constant `1_000` in `DeviceChannelManager` for trap channels (since capacity for trap channels is a locked decision, not a configuration value). Recommend: update the default in `ChannelsOptions` to 1,000 and use the configured value.

**Note:** `ChannelsOptions` is already in `AddSnmpConfiguration()`. Changing the default is a one-line change in `ChannelsOptions.cs`.

### Pitfall 7: Graceful Shutdown — Consumer Tasks Must Drain Before Process Exit

**What goes wrong:** `ChannelConsumerService.ExecuteAsync` awaits `Task.WhenAll(consumerTasks)`. If `CompleteAll()` is never called on the channel writers, the consumer tasks wait forever via `ReadAllAsync()`. The host times out and kills the process.

**How to avoid:** Ensure `DeviceChannelManager.CompleteAll()` is called during shutdown — either in a `IHostApplicationLifetime.ApplicationStopping` callback or in a `GracefulShutdownService`. The Simetra reference uses a `GracefulShutdownService` registered last (stops first) that calls `CompleteAll()`. For SnmpCollector, a simpler approach: register a `IHostedService.StopAsync` on a dedicated `ChannelDrainService` that calls `CompleteAll()` and then awaits `WaitForDrainAsync()` — or override `StopAsync` on `SnmpTrapListenerService` to call `CompleteAll()` after stopping the socket.

### Pitfall 8: First-Contact Logging Thread Safety

**What goes wrong:** Using a non-thread-safe `HashSet<string>` for tracking first-contact devices. Multiple UDP datagrams may arrive concurrently (especially with `Task`-per-datagram processing).

**How to avoid:** Use `ConcurrentDictionary<string, byte>` with `TryAdd()`. If `TryAdd()` returns true, this is the first trap from that device — log it. This is lock-free and correct for concurrent access.

---

## Code Examples

### Complete ProcessDatagram Logic

```csharp
// Source: adapted from src/Simetra/Services/SnmpListenerService.cs (read directly)
// Adapted for SnmpCollector's per-device community string model
private void ProcessDatagram(UdpReceiveResult result)
{
    IList<ISnmpMessage> messages;
    try
    {
        messages = MessageFactory.ParseMessages(
            result.Buffer, 0, result.Buffer.Length, _userRegistry);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(
            "Malformed SNMP packet from {SourceIp}: {Error}",
            result.RemoteEndPoint.Address, ex.Message);
        return;
    }

    foreach (var message in messages)
    {
        if (message is not TrapV2Message trapV2)
            continue;

        var senderIp = result.RemoteEndPoint.Address.MapToIPv4();
        var receivedCommunity = trapV2.Community().ToString();
        var variables = trapV2.Variables();

        // Step 1: Device lookup (O(1) FrozenDictionary)
        if (!_deviceRegistry.TryGetDevice(senderIp, out var device))
        {
            var firstVarbindOid = variables.Count > 0
                ? variables[0].Id.ToString()
                : "(no varbinds)";
            _logger.LogWarning(
                "Trap from unknown device {SourceIp}, first OID: {Oid}",
                senderIp, firstVarbindOid);
            _pipelineMetrics.IncrementTrapUnknownDevice();
            continue;
        }

        // Step 2: Community string auth (case-sensitive, RFC-compliant)
        if (!string.Equals(receivedCommunity, device.CommunityString, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Auth failure from {SourceIp} ({DeviceName}): received community '{ReceivedCommunity}'",
                senderIp, device.Name, receivedCommunity);
            _pipelineMetrics.IncrementTrapAuthFailed();
            continue;
        }

        // Step 3: First-contact logging
        if (_seenDevices.TryAdd(device.Name, 0))
        {
            _logger.LogInformation(
                "First trap received from {DeviceName} ({Ip})",
                device.Name, senderIp);
        }

        // Step 4: Route each varbind to the device channel
        foreach (var variable in variables)
        {
            var envelope = new VarbindEnvelope(
                Oid:        variable.Id.ToString(),
                Value:      variable.Data,
                TypeCode:   variable.Data.TypeCode,
                AgentIp:    senderIp,
                DeviceName: device.Name);

            _channelManager.GetWriter(device.Name).TryWrite(envelope);
            // itemDropped callback handles counter + periodic warning
        }
    }
}
```

### VarbindEnvelope (Channel Message Type)

```csharp
// New file: src/SnmpCollector/Pipeline/VarbindEnvelope.cs
using Lextm.SharpSnmpLib;
using System.Net;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Written to per-device BoundedChannel by SnmpTrapListenerService.
/// One envelope per varbind from a received trap.
/// </summary>
public sealed record VarbindEnvelope(
    string Oid,
    ISnmpData Value,
    SnmpType TypeCode,
    IPAddress AgentIp,
    string DeviceName
);
```

### DeviceChannelManager with Drop Tracking

```csharp
// New file: src/SnmpCollector/Pipeline/DeviceChannelManager.cs
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

public sealed class DeviceChannelManager : IDeviceChannelManager
{
    private readonly Dictionary<string, Channel<VarbindEnvelope>> _channels;
    private readonly ConcurrentDictionary<string, long> _dropCounters;
    private readonly ILogger<DeviceChannelManager> _logger;

    public DeviceChannelManager(
        IDeviceRegistry deviceRegistry,
        IOptions<ChannelsOptions> channelsOptions,
        ILogger<DeviceChannelManager> logger)
    {
        _logger = logger;
        _dropCounters = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        _channels = new Dictionary<string, Channel<VarbindEnvelope>>(StringComparer.Ordinal);

        var capacity = channelsOptions.Value.BoundedCapacity;

        foreach (var device in deviceRegistry.AllDevices)
        {
            var name = device.Name;
            _dropCounters[name] = 0L;

            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false
            };

            _channels[name] = Channel.CreateBounded(options, (VarbindEnvelope _) =>
            {
                var count = Interlocked.Increment(ref _dropCounters[name]);  // approximate; see note
                if (count % 100 == 0)
                {
                    _logger.LogWarning(
                        "Trap channel for {DeviceName} has dropped {Count} varbinds (capacity {Capacity})",
                        name, count, capacity);
                }
            });
        }
    }

    // ... GetWriter, GetReader, DeviceNames, CompleteAll
}
```

**Note on `_dropCounters[name]` in closure:** `ConcurrentDictionary` values are `long`. `Interlocked.Increment` requires a `ref long`. Use a wrapper class or `ref` workaround, OR use `PipelineMetricService.IncrementTrapDropped()` which is the actual counter — and maintain a separate `int` drop-count for the periodic log threshold:

```csharp
// Simpler: use a separate per-device int counter for periodic logging
private readonly Dictionary<string, int> _dropLogCounters; // access only from itemDropped callback

// itemDropped callback:
var count = ++_dropLogCounters[name];  // safe: itemDropped fires synchronously, single writer per channel
if (count % 100 == 0)
    _logger.LogWarning(...);
_pipelineMetrics.IncrementTrapDropped(name);
```

**Important:** The `itemDropped` callback is called on the writer's thread. If multiple writers exist (`SingleWriter = false`), the callback can be called concurrently. Use `Interlocked` operations or inject `PipelineMetricService` and track the log throttle with an `int` field per-channel protected by the channel's own internal lock (not needed — just use an atomic long or accept occasional double-logging for simplicity).

### PipelineMetricService Extension

```csharp
// MODIFIED: src/SnmpCollector/Telemetry/PipelineMetricService.cs
// Add three new fields and methods to the existing class:

private readonly Counter<long> _trapAuthFailed;
private readonly Counter<long> _trapUnknownDevice;
private readonly Counter<long> _trapDropped;

// In constructor, after existing counter creation:
_trapAuthFailed    = _meter.CreateCounter<long>("snmp.trap.auth_failed");
_trapUnknownDevice = _meter.CreateCounter<long>("snmp.trap.unknown_device");
_trapDropped       = _meter.CreateCounter<long>("snmp.trap.dropped");

// New public methods:
/// <summary>Increment the count of traps rejected due to community string mismatch.</summary>
public void IncrementTrapAuthFailed()
    => _trapAuthFailed.Add(1, new TagList { { "site_name", _siteName } });

/// <summary>Increment the count of traps from unregistered device IPs.</summary>
public void IncrementTrapUnknownDevice()
    => _trapUnknownDevice.Add(1, new TagList { { "site_name", _siteName } });

/// <summary>Increment the count of varbinds dropped due to channel backpressure.</summary>
public void IncrementTrapDropped(string deviceName)
    => _trapDropped.Add(1, new TagList
        { { "site_name", _siteName }, { "device_name", deviceName } });
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| SharpSnmpLib `SnmpEngine` / `TrapListener` class | Raw `UdpClient` + `MessageFactory.ParseMessages()` | SharpSnmpLib 12.x modernized API | Engine approach requires more configuration; raw UdpClient is simpler for BackgroundService |
| Blocking `UdpClient.Receive()` | `await udpClient.ReceiveAsync(stoppingToken)` | .NET 5+ | Cancellation-aware, no thread blocking |
| `Channel.CreateBounded()` with blocking Write | `DropOldest` with `itemDropped` callback | .NET 8+ (callback added to overload) | Fire-and-forget writes that never block, with drop observation |

**Deprecated/outdated:**
- `SnmpEngine` (SharpSnmpLib): Works but requires complex handler mapping setup. Raw `UdpClient` + `MessageFactory` is simpler for a BackgroundService pattern.
- `ForeachAwaitPublisher` (MediatR): Still default but wrong for INotification. Not relevant here since `SnmpOidReceived : IRequest<Unit>` uses `ISender.Send()` which always uses the behavior pipeline regardless of publisher type.

---

## Claude's Discretion Recommendations

These are the "Claude decides" items from CONTEXT.md, with research-backed recommendations:

### Check Ordering (auth before or after device lookup)
**Recommendation:** Device lookup FIRST, then auth check.
**Reason:** The per-device expected community string lives on `DeviceInfo.CommunityString`, which is only available after the device lookup. There is no meaningful "auth before lookup" option in this design because the auth material is device-specific. The practical ordering is: (1) unknown device check + `snmp.trap.unknown_device`, (2) community string auth + `snmp.trap.auth_failed`.

### Periodic Drop Warning Interval
**Recommendation:** Every 100 drops per device.
**Reason:** At 1,000 traps/second (1,000 capacity, steady drop rate), every-100-drops gives 10 warnings/second — visible but not overwhelming. At lower storm rates (100 traps/sec over capacity), 1 warning per second. This matches the operational intent: "periodic, not every drop."

### ChannelConsumerService Design
**Recommendation:** One `Task` per device via `ReadAllAsync()` + `Task.WhenAll`. Not a single consumer with channel multiplexing.
**Reason:** Per-device tasks provide natural isolation (one stuck device doesn't delay others), easy cancellation via the `stoppingToken` passed to `ReadAllAsync`, and graceful drain when writers are marked complete. This matches the Simetra reference exactly.

### DeviceChannelManager IP-to-Channel Mapping
**Recommendation:** Map by device name (string), not IP. The listener does the IP→name resolution via `DeviceRegistry`, then accesses the channel by name.
**Reason:** Multiple IP addresses could theoretically map to one device (future-proofing). Device name is the stable identifier used throughout the system (Quartz job keys, labels, logs). The Simetra reference uses name-keyed channels.

### SharpSnmpLib Built-in Listener vs Raw UDP Socket
**Recommendation:** Raw `UdpClient` + `MessageFactory.ParseMessages()`.
**Reason:** The Simetra reference uses this pattern (HIGH confidence, read directly). SharpSnmpLib's `SnmpEngine` requires `ObjectIdentifier`-keyed handler mapping that adds complexity without benefit for a simple v2c trap listener. `MessageFactory.ParseMessages()` is the lower-level API that gives direct access to `TrapV2Message` with no framework overhead.

---

## Open Questions

1. **`ChannelsOptions.BoundedCapacity` default vs locked-at-1000**
   - What we know: CONTEXT.md locks capacity at 1,000. `ChannelsOptions` currently defaults to 100.
   - What's unclear: Should the default be updated to 1,000 (config-driven), or should `DeviceChannelManager` use `1_000` as a constant (ignoring config)?
   - Recommendation: Update `ChannelsOptions.BoundedCapacity` default to 1,000 and use the config value. This keeps capacity configurable for testing (small value = easier to trigger backpressure in tests) while defaulting to the locked value.

2. **Graceful Shutdown: Who calls `DeviceChannelManager.CompleteAll()`?**
   - What we know: `ChannelConsumerService` drains when writers are complete. But `IHostedService.StopAsync` is called when the host stops — the `ExecuteAsync` CancellationToken is cancelled.
   - What's unclear: If `stoppingToken` is cancelled before `CompleteAll()` is called, `ReadAllAsync(stoppingToken)` throws `OperationCanceledException` immediately (no drain). If `CompleteAll()` is called first, `ReadAllAsync()` drains then completes.
   - Recommendation: Override `StopAsync` on `SnmpTrapListenerService` to: (1) cancel the receive loop, (2) call `_channelManager.CompleteAll()`. This ensures consumers drain before the host forces cancellation. Alternatively, register a separate `ChannelDrainService` that runs in `StopAsync`.

3. **`itemDropped` callback with `SingleWriter = false` — ConcurrentDictionary `ref long` limitation**
   - What we know: `Interlocked.Increment(ref dict[key])` does not compile — `ConcurrentDictionary` indexer returns by value, not by reference.
   - Recommendation: Use a plain `Dictionary<string, long>` (initialized at construction, keys never change after construction) and rely on the fact that `itemDropped` fires synchronously on the writer thread. If `SingleWriter = false` (multiple writers), the callback may be called concurrently. Use an `int[]` indexed by device index, or use `_pipelineMetrics.IncrementTrapDropped()` for the counter (thread-safe via `Counter<T>.Add`) and a separate `long` per-device for log throttle accessed via a wrapper class.

---

## Sources

### Primary (HIGH confidence)

- `src/Simetra/Services/SnmpListenerService.cs` — UdpClient receive loop, MessageFactory.ParseMessages, TrapV2Message.Community(), Variables(), community string check, device lookup pattern (read directly)
- `src/Simetra/Pipeline/DeviceChannelManager.cs` — BoundedChannel with DropOldest, itemDropped callback, SingleWriter/SingleReader options, CompleteAll pattern (read directly)
- `src/Simetra/Services/ChannelConsumerService.cs` — one Task per device, ReadAllAsync, Task.WhenAll pattern (read directly)
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — IRequest<Unit> shape, DeviceName nullable, Source field, TypeCode field (read directly)
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — TryGetDevice by IP, AllDevices, FrozenDictionary pattern (read directly)
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` — CommunityString field (resolved at startup), PollGroups (read directly)
- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` — unknown device check for trap path (DeviceName null check + TryGetDevice), sets DeviceName in-place (read directly)
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — existing counter pattern, IncrementTrapReceived, AddCounter pattern (read directly)
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — AddSnmpPipeline, ISender injection, existing service registration order (read directly)
- [Microsoft Docs: System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) — BoundedChannelOptions, DropOldest, itemDropped callback signature, ReadAllAsync pattern (fetched directly)

### Secondary (MEDIUM confidence)

- `src/SnmpCollector/Pipeline/SnmpSource.cs` — Poll/Trap enum (read directly)
- `src/SnmpCollector/Configuration/ChannelsOptions.cs` — BoundedCapacity default (read directly)
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` — community string fallback test confirms DeviceInfo.CommunityString behavior (read directly)

### Tertiary (LOW confidence)

- WebSearch for SharpSnmpLib TrapV2Message API — confirms MessageFactory.ParseMessages is the correct approach for raw UDP; Community() and Variables() are extension methods. Not authoritative but consistent with Simetra reference code.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages already in project, no new dependencies; Simetra reference read directly
- Architecture: HIGH — Simetra reference implementation for all three services read directly; Channel API verified via official MS Docs
- Pitfalls: HIGH — most derived from direct code reading; ISender vs IPublisher pitfall confirmed by Phase 3 research + codebase verification

**Research date:** 2026-03-05
**Valid until:** 2026-06-05 (stable library versions; SharpSnmpLib 12.5.7, .NET 9 BCL, MediatR 12.5.0 — 90 days reasonable)
