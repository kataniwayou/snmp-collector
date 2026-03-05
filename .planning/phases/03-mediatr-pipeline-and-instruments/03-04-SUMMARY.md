---
phase: 03-mediatr-pipeline-and-instruments
plan: 04
subsystem: telemetry
tags: [otel, opentelemetry, metrics, snmp, gauge, counter, mediatR, sharpsnmplib, concurrentdictionary]

# Dependency graph
requires:
  - phase: 03-01
    provides: SnmpOidReceived, SnmpSource, PipelineMetricService, TelemetryConstants.MeterName
  - phase: 02-02
    provides: OidMapService.Unknown constant
  - phase: 01-03
    provides: TelemetryConstants.MeterName, SiteOptions

provides:
  - ISnmpMetricFactory interface (RecordGauge, RecordInfo)
  - SnmpMetricFactory with ConcurrentDictionary instrument cache for snmp_gauge and snmp_info
  - OtelMetricHandler terminal MediatR handler dispatching by SnmpType to OTel instruments
  - snmp_gauge Gauge<double> with 5 labels: site_name, metric_name, oid, agent, source
  - snmp_info Gauge<double> with 6th label 'value' truncated at 128 chars
  - Counter32/Counter64 deferred (logged at Debug, not recorded)

affects:
  - 03-05 (DI registration for ISnmpMetricFactory, OtelMetricHandler)
  - 04-counter-delta-engine (Counter32/Counter64 recording uses ISnmpMetricFactory)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - ConcurrentDictionary<string, object> instrument cache keyed by instrument name
    - Concrete cast per SnmpType arm (Integer32.ToInt32, Gauge32/TimeTicks.ToUInt32) -- ISnmpData has no shared numeric accessor
    - ISnmpMetricFactory as DI boundary isolating OTel instrument lifecycle from handler logic

key-files:
  created:
    - src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs
    - src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
    - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
  modified: []

key-decisions:
  - "ISnmpData has no ToInt32()/ToUInt32() -- cast to concrete Integer32/Gauge32/TimeTicks per switch arm; this is safe because switch is already on TypeCode"
  - "snmp_gauge and snmp_info are both Gauge<double> cached in a ConcurrentDictionary<string, object> since Gauge<T> and Counter<T> share no common generic base"
  - "Counter32/Counter64 deferred to Phase 4 delta engine -- LogDebug emitted, IncrementHandled NOT called, no metric recorded"
  - "snmp_info value label truncated at 128 chars (125 + '...') to bound OTel label cardinality"

patterns-established:
  - "TypeCode dispatch: switch(notification.TypeCode) with per-arm concrete cast -- consistent pattern for all future SNMP type handlers"
  - "Agent resolution: DeviceName ?? AgentIp.ToString() -- poll path has DeviceName, trap path falls back to IP"
  - "Source normalization: SnmpSource.ToString().ToLowerInvariant() -- yields 'poll' or 'trap' as label value"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 3 Plan 04: SNMP Metric Instruments and OtelMetricHandler Summary

**ConcurrentDictionary-cached snmp_gauge and snmp_info OTel instruments with TypeCode-dispatch handler; Counter32/Counter64 deferred to Phase 4 delta engine**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-05T01:38:05Z
- **Completed:** 2026-03-05T01:40:06Z
- **Tasks:** 2
- **Files modified:** 3 created, 0 modified

## Accomplishments

- ISnmpMetricFactory interface and SnmpMetricFactory with ConcurrentDictionary instrument cache
- snmp_gauge records Integer32/Gauge32/TimeTicks values with 5-label TagList
- snmp_info records string OID values as 1.0 with a 6th 'value' label, truncated to 128 chars
- OtelMetricHandler dispatches by SnmpType to correct instrument, defers counters, drops unknowns

## Task Commits

Each task was committed atomically:

1. **Task 1: ISnmpMetricFactory interface and SnmpMetricFactory with instrument caching** - `1772af3` (feat)
2. **Task 2: OtelMetricHandler with TypeCode-to-instrument dispatch** - `e207e93` (feat)

## Files Created/Modified

- `src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs` - Interface declaring RecordGauge and RecordInfo
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` - ConcurrentDictionary-backed Gauge<double> instrument cache
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` - Terminal MediatR handler dispatching SNMP values to OTel instruments

## Decisions Made

- `ISnmpData` has no shared numeric accessor (`ToInt32()` is on `Integer32` only, `ToUInt32()` on `Gauge32`/`TimeTicks`). Each switch arm casts to the correct concrete type. This is safe because the switch is already discriminated by `TypeCode`.
- `snmp_gauge` and `snmp_info` are both `Gauge<double>` stored as `object` in `ConcurrentDictionary<string, object>` because `Gauge<T>` and `Counter<T>` have no shared generic base class in .NET OTel.
- Counter deferred: `Counter32`/`Counter64` log at Debug with no metric and no `IncrementHandled()` call, per plan mandate to implement delta correctness in Phase 4.
- `snmp_info` value label truncated at 128 characters (125 + "...") to prevent unbounded cardinality from long string OID values.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed ISnmpData.ToInt32() call -- method does not exist on ISnmpData interface**

- **Found during:** Task 2 (OtelMetricHandler implementation)
- **Issue:** Plan specified `Convert.ToDouble(n.Value.ToInt32())` but `ISnmpData` has no `ToInt32()` method. The method exists only on the concrete `Integer32` class. Gauge32 and TimeTicks use `ToUInt32()`.
- **Fix:** Each gauge case arm casts to the correct concrete type: `((Integer32)notification.Value).ToInt32()`, `((Gauge32)notification.Value).ToUInt32()`, `((TimeTicks)notification.Value).ToUInt32()`. The cast is safe within each arm because the switch already discriminates on TypeCode.
- **Files modified:** `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs`
- **Verification:** `dotnet build` zero errors, zero warnings
- **Committed in:** `e207e93` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Required fix for compilation correctness. No scope creep. Handler behavior is exactly as specified.

## Issues Encountered

None beyond the ISnmpData API mismatch documented above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ISnmpMetricFactory and SnmpMetricFactory ready for DI registration in 03-05
- OtelMetricHandler ready for DI registration as INotificationHandler<SnmpOidReceived> in 03-05
- Phase 4 delta engine will use ISnmpMetricFactory.RecordGauge for Counter32/Counter64 after computing deltas
- GetOrCreateCounter stub is present in SnmpMetricFactory for Phase 4 to promote to use

---
*Phase: 03-mediatr-pipeline-and-instruments*
*Completed: 2026-03-05*
