# Phase 4: Counter Delta Engine - Research

**Researched:** 2026-03-05
**Domain:** SNMP counter delta computation, OTel Counter<double> instrument, ConcurrentDictionary state, MediatR integration patterns
**Confidence:** HIGH

## Summary

Phase 4 builds the counter delta engine on top of the existing MediatR pipeline from Phase 3. The codebase is
clean and well-understood: the entry point is `OtelMetricHandler.cs` where Counter32/Counter64 switch arms
already exist and explicitly log "deferred to Phase 4". The engine needs a stateful service with a
`ConcurrentDictionary` delta cache, a `RecordCounter` method on `ISnmpMetricFactory`/`SnmpMetricFactory`,
and sysUpTime tracking per device. No new infrastructure is required.

The key technical questions for this phase are: (1) where to place the delta engine service relative to
the MediatR pipeline, (2) how sysUpTime flows from the `SnmpOidReceived` notification to the engine, and
(3) how to structure test scenarios covering all five delta paths. All three have clear answers from
codebase inspection, covered below.

**Primary recommendation:** Implement the delta engine as a plain singleton service (`ICounterDeltaEngine`)
injected directly into `OtelMetricHandler`, not as a new `IPipelineBehavior`. The handler already owns
counter dispatch; adding a behavior would split the logic across two places without benefit. The sysUpTime
value should be carried as a nullable property on `SnmpOidReceived` — populated at poll time (Phase 6)
or supplied synthetically in tests.

## Standard Stack

Phase 4 uses only libraries already in the project. No new NuGet packages are needed.

### Core (already referenced)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.Collections.Concurrent` | BCL (.NET 9) | `ConcurrentDictionary` for delta cache | Thread-safe, no lock needed for GetOrAdd pattern |
| `System.Diagnostics.Metrics` | BCL (.NET 9) | `Counter<double>` instrument via existing `Meter` | Already used in `SnmpMetricFactory` and `PipelineMetricService` |
| `Lextm.SharpSnmpLib` | 12.5.7 | `Counter32.ToUInt32()`, `Counter64.ToUInt64()`, `TimeTicks.ToUInt32()` | Already in project; confirmed API exists in `SnmpExtractorService.cs` |
| `MediatR` | 12.5.0 | `IRequestHandler` — delta engine called from handler, not a new behavior | Already wired |
| `Microsoft.Extensions.Logging` | 9.0.0 | Logging at Debug/Information per decision | Already in project |
| `xunit` | 2.9.3 | Unit test framework | Already in test project |

### No New Packages

The `GetOrCreateCounter` private method is already stubbed in `SnmpMetricFactory` (line 73-74). The
`ISnmpMetricFactory` interface just needs `RecordCounter` added. All other building blocks exist.

## Architecture Patterns

### Recommended Project Structure

```
src/SnmpCollector/
├── Pipeline/
│   ├── Handlers/
│   │   └── OtelMetricHandler.cs           # MODIFY: replace deferred arms with delta engine call
│   └── CounterDeltaEngine.cs              # NEW: ICounterDeltaEngine + CounterDeltaEngine
├── Telemetry/
│   ├── ISnmpMetricFactory.cs              # MODIFY: add RecordCounter method
│   └── SnmpMetricFactory.cs               # MODIFY: implement RecordCounter (GetOrCreateCounter exposed)

tests/SnmpCollector.Tests/
├── Pipeline/
│   └── CounterDeltaEngineTests.cs         # NEW: all 5 delta scenarios unit-tested in isolation
└── Helpers/
    └── TestSnmpMetricFactory.cs           # MODIFY: add CounterRecords list + RecordCounter stub
```

Note: `SnmpOidReceived.cs` needs a `SysUpTime` property added (nullable `uint?`). This carries the
bundled sysUpTime value from the poll path (Phase 6) or is null in tests that don't exercise reboot
detection.

### Pattern 1: Singleton Delta Cache Service

**What:** A singleton service keyed by `(oid, agent)` string that stores the last cumulative value and
the last observed sysUpTime per device IP.

**When to use:** Stateful per-OID-per-agent tracking where the set of keys is bounded by device config.
No eviction needed because the key space does not grow unboundedly.

**Cache key format:** `$"{oid}|{agent}"` — the pipe character does not appear in OID strings or device
names. This satisfies DELT-05 (independent state per OID+agent combination).

**sysUpTime storage:** Separate `ConcurrentDictionary<string, uint>` keyed by agent (device name or IP),
not by OID. One sysUpTime value per device, shared across all OIDs for that device.

**Example — service skeleton:**

```csharp
// Source: codebase pattern (SnmpMetricFactory uses same ConcurrentDictionary<string,object> pattern)
public interface ICounterDeltaEngine
{
    /// <summary>
    /// Compute and record a counter delta. Returns true if a delta was emitted, false on first-poll baseline.
    /// </summary>
    bool RecordDelta(
        string oid,
        string agent,
        string source,
        string metricName,
        SnmpType typeCode,
        ulong currentValue,
        uint? sysUpTimeCentiseconds);
}

public sealed class CounterDeltaEngine : ICounterDeltaEngine
{
    private const ulong Counter32Max = 4_294_967_296UL; // 2^32

    // Key: "oid|agent"
    private readonly ConcurrentDictionary<string, ulong> _lastValues = new();
    // Key: agent (device name/IP) — one sysUpTime per device
    private readonly ConcurrentDictionary<string, uint> _lastSysUpTimes = new();

    private readonly ISnmpMetricFactory _metricFactory;
    private readonly ILogger<CounterDeltaEngine> _logger;

    // ...
}
```

### Pattern 2: Delta Computation Logic (All 5 Paths)

The five paths the engine must handle, with the exact arithmetic:

**Path 1 — Normal increment (current >= previous):**
```csharp
double delta = current - previous;
```

**Path 2 — Counter32 wrap-around (current < previous, sysUpTime increased or unavailable but Counter32):**
```csharp
// Only applies when typeCode == SnmpType.Counter32
double delta = (Counter32Max - previous) + current;
// Example: previous=4_294_967_200, current=100 → delta = (4294967296 - 4294967200) + 100 = 196
```

**Path 3 — Reboot detected (sysUpTime decreased since last poll):**
```csharp
// sysUpTime is a TimeTicks value: centiseconds since boot. Decrease = device rebooted.
// Treat current value as delta (device counter starts from near-zero post-reboot).
double delta = current;
_logger.LogInformation("Reboot detected for agent={Agent} oid={Oid}: sysUpTime decreased", agent, oid);
```

**Path 4 — Counter64 current < previous (always reboot, never wrap):**
```csharp
// Counter64 wrap is unreachable in practice; treat as reboot.
double delta = current;
_logger.LogInformation("Reboot detected (Counter64 decreased) for agent={Agent} oid={Oid}", agent, oid);
```

**Path 5 — First poll (no previous value in cache):**
```csharp
// Store baseline, emit nothing.
_lastValues[key] = current;
_logger.LogDebug("First poll baseline stored for oid={Oid} agent={Agent}", oid, agent);
return false;
```

**All deltas clamped to non-negative (safety net):**
```csharp
delta = Math.Max(0.0, delta);
```

**Disambiguation logic (Counter32 only):**
```csharp
// After establishing previous exists and current < previous:
bool rebooted = sysUpTimeCentiseconds.HasValue
    && _lastSysUpTimes.TryGetValue(agent, out var lastUpTime)
    && sysUpTimeCentiseconds.Value < lastUpTime;

if (!rebooted && typeCode == SnmpType.Counter32)
{
    // wrap-around
    delta = (Counter32Max - previous) + current;
    _logger.LogDebug("Counter32 wrap-around oid={Oid} agent={Agent}", oid, agent);
}
else
{
    // reboot (or sysUpTime unavailable — conservative = assume reboot)
    delta = current;
    _logger.LogInformation("Reboot detected oid={Oid} agent={Agent}", oid, agent);
}
```

### Pattern 3: OtelMetricHandler Integration

The existing Counter32/Counter64 switch arms in `OtelMetricHandler` currently log and skip. Phase 4
replaces them with a call to the injected `ICounterDeltaEngine`:

```csharp
// Source: existing OtelMetricHandler.cs switch, lines 72-80 — replace with:
case SnmpType.Counter32:
{
    var currentValue = ((Counter32)notification.Value).ToUInt32();
    var didEmit = _deltaEngine.RecordDelta(
        notification.Oid,
        agent,
        source,
        metricName,
        SnmpType.Counter32,
        currentValue,
        notification.SysUpTimeCentiseconds);
    if (didEmit) _pipelineMetrics.IncrementHandled();
    break;
}

case SnmpType.Counter64:
{
    var currentValue = (ulong)((Counter64)notification.Value).ToUInt64();
    var didEmit = _deltaEngine.RecordDelta(
        notification.Oid,
        agent,
        source,
        metricName,
        SnmpType.Counter64,
        currentValue,
        notification.SysUpTimeCentiseconds);
    if (didEmit) _pipelineMetrics.IncrementHandled();
    break;
}
```

Note: `Counter64.ToUInt64()` returns `ulong` (see `SnmpExtractorService.cs` line 100 as reference).
The internal cache must use `ulong` to accommodate Counter64 values without truncation.

### Pattern 4: RecordCounter on ISnmpMetricFactory

`Counter<double>.Add()` is the OTel instrument method — not `Record()` like `Gauge<double>`. The existing
`GetOrCreateCounter` private method in `SnmpMetricFactory` uses `_meter.CreateCounter<double>`.

```csharp
// Add to ISnmpMetricFactory interface:
void RecordCounter(string metricName, string oid, string agent, string source, double delta);

// Implementation in SnmpMetricFactory (exposes existing private helper):
public void RecordCounter(string metricName, string oid, string agent, string source, double delta)
{
    var counter = GetOrCreateCounter("snmp_counter");
    counter.Add(delta, new TagList
    {
        { "site_name", _siteName },
        { "metric_name", metricName },
        { "oid", oid },
        { "agent", agent },
        { "source", source }
    });
}
```

The same 5-label taxonomy (site_name, metric_name, oid, agent, source) as `snmp_gauge` — no `value` label
since this is a numeric delta instrument, not an info metric.

### Pattern 5: SnmpOidReceived SysUpTime Property

The `SnmpOidReceived` class uses `set` (not `init`) for properties that are enriched post-construction
(pattern already established for `DeviceName`). `SysUpTimeCentiseconds` follows the same pattern:

```csharp
// Add to SnmpOidReceived.cs:
/// <summary>
/// sysUpTime value (OID 1.3.6.1.2.1.1.3.0) in centiseconds, bundled with the counter poll.
/// Null when unavailable (device doesn't expose it, SNMP error, or test without uptime context).
/// When null, engine conservatively treats current < previous as reboot.
/// </summary>
public uint? SysUpTimeCentiseconds { get; set; }
```

`TimeTicks.ToUInt32()` returns `uint` (centiseconds) — confirmed in `OtelMetricHandler.cs` line 67 where
`((TimeTicks)notification.Value).ToUInt32()` is already used for the TimeTicks gauge case.

### Anti-Patterns to Avoid

- **Do not add a new IPipelineBehavior for delta computation.** The counter logic belongs in
  `OtelMetricHandler` where the Counter32/Counter64 dispatch already lives. Splitting it into a behavior
  adds indirection with no benefit, and the behavior pattern is already used for cross-cutting concerns
  (logging, validation, OID resolution), not business logic.

- **Do not key the cache by OID alone.** Two devices reporting the same OID (e.g., interface counters)
  must maintain independent delta state. Key must be `oid|agent` (DELT-05).

- **Do not use `long` for the cache value type.** Counter64 values can exceed `long.MaxValue` (2^63-1).
  Use `ulong` for the internal cache even for Counter32 — Counter32.ToUInt32() returns `uint` which
  fits in `ulong` without loss.

- **Do not use `double` for the internal cache values.** `double` loses precision for large Counter64
  values (doubles have 53-bit mantissa; Counter64 is 64-bit). Store raw `ulong` in cache, convert to
  `double` only when calling `RecordCounter`.

- **Do not call `IncrementHandled()` on first-poll baseline.** The counter was not recorded; nothing was
  handled in the metric sense. The existing Phase 3 pattern defers `IncrementHandled` to when a metric
  is actually emitted.

- **Do not make the delta engine a scoped service.** It holds per-process state that must persist across
  requests. It must be `AddSingleton`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Thread-safe delta cache | Custom locking | `ConcurrentDictionary.GetOrAdd` / `AddOrUpdate` | Already used in `SnmpMetricFactory`; handles concurrent SNMP poll paths without explicit locks |
| OTel counter instrument | Custom counter abstraction | `Meter.CreateCounter<double>` + `Counter<double>.Add` | Already stubbed in `SnmpMetricFactory.GetOrCreateCounter`; just needs to be made accessible |
| Counter type extraction | Custom type inspection | `Counter32.ToUInt32()`, `Counter64.ToUInt64()` | Confirmed in `SnmpExtractorService.cs`; SharpSnmpLib provides these directly |

**Key insight:** `SnmpMetricFactory` already has `GetOrCreateCounter` as a private method (line 73-74).
The only work needed is making it callable via `RecordCounter` on the public interface.

## Common Pitfalls

### Pitfall 1: ConcurrentDictionary Race on First-Poll Detection

**What goes wrong:** Two concurrent polls for the same OID+agent arrive simultaneously. Both find no
entry in the cache. Both try to set the baseline. One wins but the other may compute a spurious delta
of zero (current - current = 0) or emit a false metric.

**Why it happens:** `GetOrAdd` for the "does entry exist?" check followed by a separate `TryUpdate`
is not atomic.

**How to avoid:** Use `AddOrUpdate` with a factory delegate that returns the current value for "first
add" (stores baseline) and returns the new value for "update" (computes delta inline). Or alternatively,
use a record/struct cache entry with a `hasBaseline` flag to distinguish "first add" from "subsequent
update" atomically.

Simplest correct pattern:
```csharp
// Atomic: if key is new, store current as baseline and return false (no emission).
// If key exists, compute delta and update to current.
ulong? previousValue = null;
_lastValues.AddOrUpdate(
    key,
    addValueFactory: _ => current,          // first poll: store baseline
    updateValueFactory: (_, prev) =>
    {
        previousValue = prev;               // capture previous for delta computation
        return current;                     // update cache to current
    });

if (previousValue is null) return false;   // first poll, baseline stored
// proceed to delta computation with previousValue.Value
```

**Warning signs:** Tests showing delta=0 on second poll with the same value, or tests showing a delta
being emitted on first poll.

### Pitfall 2: double Precision Loss for Counter64

**What goes wrong:** Storing Counter64 cache values as `double` loses precision for counters near 2^53
(~9 quadrillion). A high-traffic 64-bit interface counter can legitimately reach this range over months.
When precision is lost, delta computation produces wrong (often negative or zero) results.

**Why it happens:** `double` has a 53-bit mantissa. Counter64 is a 64-bit unsigned integer. Values above
2^53 cannot be represented exactly in `double`.

**How to avoid:** Cache type is `ulong`. Convert to `double` only at the final `counter.Add(delta, ...)` call.
The `delta` itself (current - previous) fits in `double` safely as long as individual deltas are not
astronomically large.

### Pitfall 3: sysUpTime Rollover Confusion (TimeTicks Wraps at 2^32 Centiseconds)

**What goes wrong:** `TimeTicks` is a 32-bit unsigned integer in SNMP. It wraps to zero after
approximately 497 days of uptime (2^32 centiseconds / 100 / 3600 / 24 ≈ 497 days). A device that has
been running for >497 days will have its sysUpTime wrap around to zero — this looks like a reboot but
isn't.

**Why it happens:** The sysUpTime OID (1.3.6.1.2.1.1.3.0) uses `TimeTicks` which has a 32-bit range.

**How to avoid:** For Phase 4, this is a known limitation. The context decisions specify
"if sysUpTime decreased → reboot detected" without carving out the >497-day edge case. This is acceptable
because:
1. The consequence is treating a rare sysUpTime wrap as a reboot, causing one poll to report current value
   as delta (conservative but not catastrophic)
2. Counter64 wraps are anyway treated as reboot unconditionally
3. A device running continuously for 497+ days without a reboot is unusual in the monitored environments

Do **not** add special logic to handle this case in Phase 4 — it's in scope only to know it exists.

### Pitfall 4: Counter<double>.Add vs Gauge<double>.Record

**What goes wrong:** Using `gauge.Record(delta, tags)` instead of `counter.Add(delta, tags)` for the
`snmp_counter` instrument. `Record` is the method on `Gauge<T>`; `Add` is the method on `Counter<T>`.
These are different instruments with different semantic guarantees in OTel.

**Why it happens:** Both are `System.Diagnostics.Metrics` types and look similar. The existing gauge code
uses `Record`; someone copy-pasting it to a counter will get a compile error (Counter<T> has no `Record`
method) — but the same mistake in reverse (calling `Add` on a Gauge) would not compile either. The
compile error is the safeguard here.

**How to avoid:** The `GetOrCreateCounter` stub in `SnmpMetricFactory` already returns `Counter<double>`.
Use `.Add(delta, tagList)` on it, consistent with how `PipelineMetricService` uses counters.

### Pitfall 5: TestSnmpMetricFactory Missing CounterRecords

**What goes wrong:** Phase 3's `TestSnmpMetricFactory` only has `GaugeRecords` and `InfoRecords`. Adding
`RecordCounter` to `ISnmpMetricFactory` without updating `TestSnmpMetricFactory` breaks all existing
tests that use it.

**Why it happens:** The test helper implements the interface explicitly. Adding a method to the interface
causes a compile error in `TestSnmpMetricFactory`.

**How to avoid:** Add `CounterRecords` list and `RecordCounter` stub to `TestSnmpMetricFactory` as part
of the same task that modifies `ISnmpMetricFactory`. This is a required change, not optional.

## Code Examples

Verified patterns from the existing codebase:

### Counter value extraction (from SnmpExtractorService.cs)
```csharp
// Source: src/Simetra/Services/SnmpExtractorService.cs lines 99-100
Counter32 c32 => c32.ToUInt32(),   // returns uint
Counter64 c64 => c64.ToUInt64(),   // returns ulong
```

### Existing GetOrCreateCounter stub (from SnmpMetricFactory.cs lines 72-74)
```csharp
// Source: src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
// "Exposed for future Phase 4 counter delta engine."
private Counter<double> GetOrCreateCounter(string name)
    => (Counter<double>)_instruments.GetOrAdd(name, n => _meter.CreateCounter<double>(n));
```

### OTel Counter<double>.Add with TagList (from PipelineMetricService.cs)
```csharp
// Source: src/SnmpCollector/Telemetry/PipelineMetricService.cs line 51
_published.Add(1, new TagList { { "site_name", _siteName } });
// snmp_counter will use the same 5-label TagList pattern as snmp_gauge
```

### MeterListener in tests (from SnmpMetricFactoryTests.cs)
```csharp
// Source: tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs lines 32-42
_listener = new MeterListener();
_listener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == TelemetryConstants.MeterName)
        listener.EnableMeasurementEvents(instrument);
};
_listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
{
    _recordedTags.Add(tags.ToArray());
});
_listener.Start();
```

Use this exact pattern in `CounterDeltaEngineTests` when verifying that `snmp_counter` gets the correct
tags. Alternatively, use `TestSnmpMetricFactory.CounterRecords` for simpler assertion without OTel wiring.

### ConcurrentDictionary AddOrUpdate for atomic baseline/update
```csharp
// Source: BCL pattern — consistent with SnmpMetricFactory's GetOrAdd usage
ulong? previousValue = null;
_lastValues.AddOrUpdate(
    key,
    addValueFactory: _ => current,
    updateValueFactory: (_, prev) =>
    {
        previousValue = prev;
        return current;
    });
if (previousValue is null) return false; // first poll
```

### sysUpTime disambiguation (after previous value is known)
```csharp
// Determine if current < previous is wrap or reboot:
bool sysUpTimeDecreased =
    sysUpTimeCentiseconds.HasValue &&
    _lastSysUpTimes.TryGetValue(agent, out var lastUpTime) &&
    sysUpTimeCentiseconds.Value < lastUpTime;

// Update sysUpTime regardless (so next poll has fresh baseline)
if (sysUpTimeCentiseconds.HasValue)
    _lastSysUpTimes[agent] = sysUpTimeCentiseconds.Value;

double delta;
if (current >= previousValue)
{
    delta = current - previousValue;
}
else if (!sysUpTimeDecreased && typeCode == SnmpType.Counter32)
{
    // wrap-around
    delta = (Counter32Max - previousValue) + current;
    _logger.LogDebug("Counter32 wrap-around: Oid={Oid} Agent={Agent}", oid, agent);
}
else
{
    // reboot (sysUpTime decreased, or unavailable, or Counter64)
    delta = current;
    _logger.LogInformation("Reboot detected: Oid={Oid} Agent={Agent}", oid, agent);
}

delta = Math.Max(0.0, delta); // clamp to non-negative
```

## Test Design

The test suite needs to exercise all five delta paths against `CounterDeltaEngine` in isolation (not
through the full MediatR pipeline). A helper method that injects synthetic values is simpler than
building a full pipeline:

```csharp
// Pattern: instantiate CounterDeltaEngine directly, inject TestSnmpMetricFactory
public sealed class CounterDeltaEngineTests
{
    private readonly TestSnmpMetricFactory _factory = new();
    private readonly CounterDeltaEngine _engine;

    public CounterDeltaEngineTests()
    {
        _engine = new CounterDeltaEngine(_factory, NullLogger<CounterDeltaEngine>.Instance);
    }

    [Fact]
    public void NormalIncrement_EmitsDelta500()
    {
        // First poll: baseline
        _engine.RecordDelta("1.3.6.1.2.1.2.2.1.10.1", "router-01", "poll", "ifInOctets",
            SnmpType.Counter32, 1000, sysUpTimeCentiseconds: 50000);
        Assert.Empty(_factory.CounterRecords);

        // Second poll: increment
        _engine.RecordDelta("1.3.6.1.2.1.2.2.1.10.1", "router-01", "poll", "ifInOctets",
            SnmpType.Counter32, 1500, sysUpTimeCentiseconds: 60000);
        Assert.Single(_factory.CounterRecords);
        Assert.Equal(500.0, _factory.CounterRecords[0].Delta);
    }

    [Fact]
    public void Counter32Wrap_EmitsCorrectDelta()
    {
        // previous=4_294_967_200, current=100 → delta = (2^32 - 4294967200) + 100 = 196
        _engine.RecordDelta(oid, agent, "poll", "metric", SnmpType.Counter32, 4_294_967_200, 50000);
        _engine.RecordDelta(oid, agent, "poll", "metric", SnmpType.Counter32, 100, 60000);
        Assert.Equal(196.0, _factory.CounterRecords[0].Delta);
    }

    [Fact]
    public void RebootDetected_EmitsCurrentValueAsDelta()
    {
        // sysUpTime decreases → reboot
        _engine.RecordDelta(oid, agent, "poll", "metric", SnmpType.Counter32, 5000, 90000);
        _engine.RecordDelta(oid, agent, "poll", "metric", SnmpType.Counter32, 300, 1000); // uptime reset
        Assert.Equal(300.0, _factory.CounterRecords[0].Delta);
    }

    [Fact]
    public void FirstPoll_NoEmission()
    {
        _engine.RecordDelta(oid, agent, "poll", "metric", SnmpType.Counter32, 9999, 50000);
        Assert.Empty(_factory.CounterRecords);
    }

    [Fact]
    public void TwoAgentsSameOid_IndependentState()
    {
        // Agent A baseline
        _engine.RecordDelta(oid, "agent-a", "poll", "metric", SnmpType.Counter32, 1000, 50000);
        // Agent B baseline
        _engine.RecordDelta(oid, "agent-b", "poll", "metric", SnmpType.Counter32, 5000, 50000);
        // Agent A second poll
        _engine.RecordDelta(oid, "agent-a", "poll", "metric", SnmpType.Counter32, 1100, 60000);
        // Agent B second poll
        _engine.RecordDelta(oid, "agent-b", "poll", "metric", SnmpType.Counter32, 5050, 60000);

        Assert.Equal(2, _factory.CounterRecords.Count);
        Assert.Equal(100.0, _factory.CounterRecords[0].Delta); // agent-a
        Assert.Equal(50.0, _factory.CounterRecords[1].Delta);  // agent-b
    }
}
```

`TestSnmpMetricFactory.CounterRecords` tuple type should be:
`(string MetricName, string Oid, string Agent, string Source, double Delta)`

## DI Registration

Add to `ServiceCollectionExtensions.AddSnmpPipeline()`:
```csharp
services.AddSingleton<ICounterDeltaEngine, CounterDeltaEngine>();
```

Add to `OtelMetricHandler` constructor:
```csharp
private readonly ICounterDeltaEngine _deltaEngine;

public OtelMetricHandler(
    ISnmpMetricFactory metricFactory,
    ICounterDeltaEngine deltaEngine,
    PipelineMetricService pipelineMetrics,
    ILogger<OtelMetricHandler> logger)
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `INotification` + `IPublisher.Publish` | `IRequest<Unit>` + `ISender.Send` | Phase 3 (03-06) | IPipelineBehavior fires; delta engine injected into handler works correctly |
| Counter arms log-and-skip | Counter arms call delta engine | Phase 4 | Counter metrics actually recorded |

**Note on IPipelineBehavior vs service:** MediatR 12 `IPipelineBehavior<TRequest, TResponse>` only fires
when dispatched via `ISender.Send` (confirmed by state decision 03-06). If someone mistakenly uses
`IPublisher.Publish`, the entire behavior chain is bypassed. The delta engine as a service injected into
the handler is immune to this mistake — it will always run when the handler runs.

## Open Questions

1. **SysUpTime null on trap path**
   - What we know: Phase 5 (trap ingestion) is out of scope for Phase 4. The `SnmpOidReceived`
     `SysUpTimeCentiseconds` property will be null for all trap-sourced notifications.
   - What's unclear: Will traps ever carry Counter32/Counter64 OIDs in practice?
   - Recommendation: Handle conservatively — null sysUpTime + current < previous → assume reboot
     (already covered by the disambiguation logic above). No special trap code path needed.

2. **ulong overflow safety when computing Counter32 wrap delta**
   - What we know: `(Counter32Max - previousValue) + current` where `previousValue` is `ulong` and
     `Counter32Max` is `4_294_967_296UL`. If somehow `previousValue > Counter32Max` (impossible for
     real Counter32 but possible if cache has been corrupted), the subtraction underflows.
   - Recommendation: For Counter32 wrap path, cast `previousValue` to `uint` before arithmetic:
     `(Counter32Max - (uint)previousValue) + current`. This makes the math self-consistent with the
     32-bit domain, regardless of what the cache holds.

## Sources

### Primary (HIGH confidence)

- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — Counter32/Counter64 switch arms to replace
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — `GetOrCreateCounter` stub at lines 72-74; `ConcurrentDictionary<string, object>` cache pattern; `Gauge<double>.Record` vs `Counter<double>.Add` distinction
- `src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs` — current interface shape; where to add `RecordCounter`
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — `set` pattern for enrichment properties; where to add `SysUpTimeCentiseconds`
- `src/Simetra/Services/SnmpExtractorService.cs` — confirmed `Counter32.ToUInt32()` and `Counter64.ToUInt64()` API
- `tests/SnmpCollector.Tests/Helpers/TestSnmpMetricFactory.cs` — must be updated when ISnmpMetricFactory changes
- `tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs` — `MeterListener` test pattern for OTel verification
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` — existing `SendCounter32_NothingRecorded` test will need updating to verify counter IS recorded after Phase 4
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — `AddSnmpPipeline` where DI registration goes

### Secondary (MEDIUM confidence)

- BCL documentation for `ConcurrentDictionary.AddOrUpdate` — atomic baseline-then-update pattern (standard .NET pattern, well-established)

### Tertiary (LOW confidence)

- None — all claims verified against actual codebase or BCL

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all libraries confirmed present in .csproj files
- Architecture: HIGH — all integration points confirmed by reading the actual source files
- Pitfalls: HIGH — race condition and precision issues identified from code review; test update requirement verified by reading TestSnmpMetricFactory
- Test design: HIGH — matches existing test patterns in SnmpMetricFactoryTests.cs and OtelMetricHandlerTests.cs

**Research date:** 2026-03-05
**Valid until:** 2026-06-05 (stable stack, no fast-moving dependencies)
