---
phase: 05-trap-ingestion
plan: 01
subsystem: pipeline
tags: [snmp, channels, backpressure, otel, dotnet, sharpsnmplib]

# Dependency graph
requires:
  - phase: 02-device-registry-and-oid-map
    provides: IDeviceRegistry.AllDevices enumeration used to create per-device channels
  - phase: 03-mediatr-pipeline-and-instruments
    provides: PipelineMetricService singleton that owns all pipeline counter instruments
provides:
  - ChannelsOptions with BoundedCapacity=1000 default and [Range] validation
  - VarbindEnvelope sealed record (Oid, Value, TypeCode, AgentIp, DeviceName)
  - IDeviceChannelManager interface (GetWriter, GetReader, DeviceNames, CompleteAll)
  - DeviceChannelManager singleton with per-device BoundedChannel, DropOldest backpressure, and lock-free drop counter
  - PipelineMetricService extended with 3 new counters: snmp.trap.auth_failed, snmp.trap.unknown_device, snmp.trap.dropped
affects: [05-02-plan, 05-03-plan, 05-04-plan]

# Tech tracking
tech-stack:
  added: [System.Threading.Channels (inbox .NET 9)]
  patterns:
    - Per-device BoundedChannel with DropOldest for trap storm protection
    - Lock-free drop counter using DropCounter sealed class with Interlocked.Increment
    - itemDropped callback overload of Channel.CreateBounded for backpressure telemetry
    - Periodic Warning log every 100 drops to avoid log flooding

key-files:
  created:
    - src/SnmpCollector/Configuration/ChannelsOptions.cs
    - src/SnmpCollector/Pipeline/VarbindEnvelope.cs
    - src/SnmpCollector/Pipeline/IDeviceChannelManager.cs
    - src/SnmpCollector/Pipeline/DeviceChannelManager.cs
  modified:
    - src/SnmpCollector/Telemetry/PipelineMetricService.cs

key-decisions:
  - "BoundedCapacity defaults to 1,000 per device per CONTEXT.md locked decision"
  - "SingleWriter=false because multiple concurrent UDP receive callbacks may write to the same device channel"
  - "SingleReader=true because ChannelConsumerService spawns exactly one Task per device"
  - "AllowSynchronousContinuations=false so listener thread is never blocked by consumer continuations"
  - "DropCounter sealed class with Interlocked.Increment chosen over ConcurrentDictionary<string, long> for correctness (ref on dict value won't compile) and lock-freedom"
  - "Warning log every 100 drops per device (not every drop) to bound log volume during trap storms"
  - "device_name tag added to snmp.trap.dropped (only) for per-device Prometheus alerting; auth_failed and unknown_device use site_name only"

patterns-established:
  - "DropCounter: private sealed class with public long field + Interlocked.Increment for lock-free per-device counters"
  - "itemDropped callback: increment metric first, then threshold-log for drop telemetry pattern"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 5 Plan 01: Trap Ingestion Foundation Types Summary

**Per-device BoundedChannel infrastructure with DropOldest backpressure, VarbindEnvelope message type, ChannelsOptions config, and 3 new PipelineMetricService counters (auth_failed, unknown_device, dropped) for trap telemetry**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-05T05:07:59Z
- **Completed:** 2026-03-05T05:09:47Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- ChannelsOptions and VarbindEnvelope foundation types created and compile cleanly
- PipelineMetricService extended from 6 to 9 counters with correct tag semantics
- IDeviceChannelManager interface and DeviceChannelManager singleton implemented with DropOldest backpressure and lock-free per-device drop counters
- All 64 existing tests continue passing (no regressions)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ChannelsOptions and VarbindEnvelope foundation types** - `c44f642` (feat)
2. **Task 2: Add 3 trap counters, IDeviceChannelManager, and DeviceChannelManager** - `7593c44` (feat)

**Plan metadata:** (docs commit to follow)

## Files Created/Modified
- `src/SnmpCollector/Configuration/ChannelsOptions.cs` - BoundedCapacity config with 1000 default and [Range(1,100_000)] validation
- `src/SnmpCollector/Pipeline/VarbindEnvelope.cs` - Sealed record carrying one varbind through per-device channels
- `src/SnmpCollector/Pipeline/IDeviceChannelManager.cs` - Interface: GetWriter, GetReader, DeviceNames, CompleteAll
- `src/SnmpCollector/Pipeline/DeviceChannelManager.cs` - Singleton creating BoundedChannel per device with DropOldest and lock-free drop telemetry
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` - 3 new counters: IncrementTrapAuthFailed, IncrementTrapUnknownDevice, IncrementTrapDropped(deviceName)

## Decisions Made
- `SingleWriter=false` on BoundedChannelOptions: multiple concurrent UDP receive callbacks on the listener may write to the same device channel simultaneously, so single-writer optimization is not safe
- `SingleReader=true`: ChannelConsumerService spawns exactly one Task per device, making single-reader optimization valid and correct
- `AllowSynchronousContinuations=false`: ensures consumer continuations do not run synchronously on the listener's thread, preventing listener backpressure from consumer slowness
- `DropCounter` private sealed class with `Interlocked.Increment` on a `long` field: `ConcurrentDictionary<string, long>` cannot use `Interlocked.Increment(ref dict[key])` (not valid C#); the DropCounter wrapper enables lock-free increment correctly
- Warning logged every 100 drops per device: bounds log volume during trap storms while still providing visibility

## Deviations from Plan

None - plan executed exactly as written. The DropCounter implementation matches the plan's recommended approach verbatim.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All types needed by Plan 05-02 (SnmpTrapListenerService) are in place: VarbindEnvelope, IDeviceChannelManager, PipelineMetricService trap counters
- All types needed by Plan 05-03 (ChannelConsumerService) are in place: IDeviceChannelManager.GetReader, DeviceNames, CompleteAll
- DeviceChannelManager must be registered as a singleton in DI before Plans 05-02 and 05-03 can start
- No blockers

---
*Phase: 05-trap-ingestion*
*Completed: 2026-03-05*
