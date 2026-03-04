# Architecture Research

**Domain:** SNMP monitoring system — C# .NET 9, MediatR pipeline, OTel push to Prometheus/Grafana
**Researched:** 2026-03-04
**Confidence:** HIGH (primary source: reference implementation at src/Simetra/ read directly)

---

## Standard Architecture

### System Overview

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                          INGESTION LAYER (Layer 1-2)                         │
│                                                                              │
│  ┌──────────────────────────┐     ┌────────────────────────────────────────┐ │
│  │  SnmpTrapListener        │     │  SnmpPoller (Quartz MetricPollJob)     │ │
│  │  (BackgroundService)     │     │  [DisallowConcurrentExecution]         │ │
│  │                          │     │                                        │ │
│  │  UDP:162 → parse →       │     │  SNMP GET → ISnmpExtractor → bypass   │ │
│  │  community check →       │     │  channels → straight to Layer 3/4     │ │
│  │  middleware pipeline →   │     └────────────────────────────────────────┘ │
│  │  device lookup →         │                      │                         │
│  │  OID filter →            │                      │ (direct, no channel)    │
│  │  channel write           │                      ↓                         │
│  └──────────┬───────────────┘                                               │
│             │                                                                │
│             ↓ BoundedChannel<TrapEnvelope>                                  │
│  ┌──────────────────────────┐                                               │
│  │  DeviceChannelManager    │  one bounded channel per device               │
│  │  DropOldest on full      │  SingleWriter=false, SingleReader=true        │
│  └──────────┬───────────────┘                                               │
│             │                                                                │
│             ↓                                                                │
│  ┌──────────────────────────┐                                               │
│  │  ChannelConsumerService  │  one Task per device channel                  │
│  │  (BackgroundService)     │  ReadAllAsync → consumer middleware →         │
│  │                          │  ISnmpExtractor → IProcessingCoordinator      │
│  └──────────┬───────────────┘                                               │
└─────────────┼────────────────────────────────────────────────────────────────┘
              │
              ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                          PIPELINE LAYER (Layer 3-4 equivalent)              │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                    MediatR Behavior Pipeline                          │  │
│  │                                                                       │  │
│  │  Publish(SnmpOidReceived)                                             │  │
│  │       ↓                                                               │  │
│  │  [LoggingBehavior]                                                    │  │
│  │       ↓                                                               │  │
│  │  [ExceptionBehavior]                                                  │  │
│  │       ↓                                                               │  │
│  │  [ValidationBehavior]                                                 │  │
│  │       ↓                                                               │  │
│  │  [OidResolutionBehavior]  ← looks up OID in flat Dictionary          │  │
│  │       ↓                                                               │  │
│  │  OtelMetricHandler        ← records snmp_gauge / snmp_counter        │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
              │
              ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                          PROCESSING LAYER                                   │
│                                                                             │
│  ProcessingCoordinator                                                      │
│  ├── Branch A: MetricFactory → System.Diagnostics.Metrics instruments      │
│  │   (always runs, both trap and poll sources)                              │
│  └── Branch B: StateVectorService → in-memory ConcurrentDictionary         │
│      (Source=Module only; Configuration polls skip this branch)             │
└─────────────────────────────────────────────────────────────────────────────┘
              │
              ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                          TELEMETRY EXPORT LAYER                             │
│                                                                             │
│  ┌────────────────────────────┐   ┌────────────────────────────────────┐   │
│  │  PeriodicExportingMetric   │   │  BatchActivityExportProcessor      │   │
│  │  Reader                    │   │  (traces, role-gated)              │   │
│  │       ↓                    │   └───────────────────┬────────────────┘   │
│  │  MetricRoleGatedExporter   │                       │                    │
│  │  ├── Simetra.Leader meter  │                       ↓                    │
│  │  │   → leader only exports │   ┌────────────────────────────────────┐   │
│  │  └── System.Runtime meter  │   │  OTLP Collector / Prometheus       │   │
│  │      → all pods export     │   │  → Grafana dashboards              │   │
│  └────────────────────────────┘   └────────────────────────────────────┘   │
│                                                                             │
│  OTLP Log Exporter (all pods, not role-gated)                              │
│  + SimetraLogEnrichmentProcessor (adds site/role/correlationId)            │
└─────────────────────────────────────────────────────────────────────────────┘
              │
              │ (parallel)
              ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                     LEADER ELECTION (K8s Lease API)                         │
│                                                                             │
│  K8sLeaseElection (BackgroundService + ILeaderElection)                     │
│  coordination.k8s.io/v1 Lease → volatile bool _isLeader                    │
│                                                                             │
│  All instances active:                                                      │
│  ├── ALL pods: pipeline metrics (Simetra.Instance meter) + logs + runtime  │
│  └── LEADER only: business metrics (Simetra.Leader meter) + traces         │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Namespace / Type | Responsibility | Communicates With |
|-----------|-----------------|----------------|-------------------|
| SnmpTrapListener | BackgroundService | Bind UDP:162, parse traps, run listener middleware pipeline, route to device channels | DeviceRegistry, TrapFilter, DeviceChannelManager, TrapMiddlewarePipeline |
| SnmpPoller (MetricPollJob) | Quartz IJob | SNMP GET at intervals, bypass channels, feed directly to extraction + processing | DeviceRegistry, PollDefinitionRegistry, ISnmpExtractor, ProcessingCoordinator |
| StatePollJob | Quartz IJob | Module-defined state polls; same bypass-channel path as MetricPollJob | Same as MetricPollJob |
| HeartbeatJob | Quartz IJob | Send SNMP trap to self (127.0.0.1) to prove pipeline alive | SharpSnmpLib Messenger |
| CorrelationJob | Quartz IJob | Rotate global correlationId on schedule | RotatingCorrelationService |
| DeviceRegistry | Singleton | Map IP → DeviceInfo and Name → DeviceInfo at O(1) | Built from DevicesOptions + IDeviceModule singletons |
| DeviceChannelManager | Singleton | One BoundedChannel<TrapEnvelope> per device; DropOldest; drain on shutdown | SnmpListenerService (writer), ChannelConsumerService (reader) |
| TrapPipelineBuilder | Singleton | Compose middleware delegates into a single TrapMiddlewareDelegate | Used by SnmpListenerService (intake) and ChannelConsumerService (consumer) |
| ChannelConsumerService | BackgroundService | One Task per device; ReadAllAsync → consumer middleware → extract → process | DeviceChannelManager, ISnmpExtractor, ProcessingCoordinator |
| MediatR Pipeline | IPipelineBehavior<T,R> | Logging → Exception → Validation → OidResolution behaviors | Published by Listener + Poller via MediatR.Publish(SnmpOidReceived) |
| OtelMetricHandler | INotificationHandler | Record snmp_gauge / snmp_counter / snmp_info based on OID TypeCode | MetricFactory (System.Diagnostics.Metrics instruments) |
| MetricFactory | Singleton | Create/cache Gauge/Counter instruments; record with base + static + dynamic labels | System.Diagnostics.Metrics Meter (Simetra.Leader) |
| StateVectorService | Singleton | ConcurrentDictionary of last ExtractionResult per device:metric key | ProcessingCoordinator (writer), future CorrelationJob or StatePoll readers |
| ProcessingCoordinator | Singleton | Branch A: metrics always; Branch B: StateVector only for Source=Module | MetricFactory, StateVectorService |
| RotatingCorrelationService | Singleton | volatile string global correlationId + AsyncLocal operation-scoped correlationId | CorrelationJob (writer), all jobs (readers), middleware (stamper) |
| K8sLeaseElection | BackgroundService + ILeaderElection | Acquire/release coordination.k8s.io/v1 Lease; expose IsLeader volatile bool | MetricRoleGatedExporter, RoleGatedExporter (traces), SimetraLogEnrichmentProcessor |
| MetricRoleGatedExporter | BaseExporter<Metric> | Gate Simetra.Leader meter behind leadership; pass System.Runtime through always | OtlpMetricExporter (inner), ILeaderElection |
| GracefulShutdownService | IHostedService (last) | Orchestrate 5-step shutdown with per-step time budgets | K8sLeaseElection, SnmpListenerService, IScheduler, DeviceChannelManager, MeterProvider |
| LivenessHealthCheck | IHealthCheck | Compare job liveness stamps against interval × grace multiplier | ILivenessVectorService, IJobIntervalRegistry |
| SimetraConsoleFormatter | ConsoleFormatter | Prefix log lines with [site] [role] [correlationId] | ICorrelationService, ILeaderElection |

---

## Recommended Project Structure

Based on the reference implementation and MediatR conventions for the new system:

```
src/
├── Monitoring.Agent/                  # Executable project
│   ├── Program.cs                     # Host builder, DI registration, health endpoints
│   ├── GlobalUsings.cs
│   └── appsettings.json               # OID map (flat Dictionary), device config, OTLP endpoint
│
├── Monitoring.Core/                   # Domain types (if multi-project; can be single project)
│   ├── Messages/
│   │   └── SnmpOidReceived.cs         # MediatR INotification — the central event
│   ├── Models/
│   │   ├── OidMapEntry.cs             # Resolved OID metadata (name, type, unit)
│   │   └── DeviceInfo.cs              # Device identity (name, IP, type)
│   └── Abstractions/
│       ├── ILeaderElection.cs
│       └── ICorrelationService.cs
│
├── Ingestion/
│   ├── SnmpTrapListener.cs            # BackgroundService → MediatR.Publish(SnmpOidReceived)
│   └── SnmpPoller/
│       └── MetricPollJob.cs           # Quartz IJob → MediatR.Publish(SnmpOidReceived)
│
├── Pipeline/
│   ├── Behaviors/
│   │   ├── LoggingBehavior.cs         # Outermost: log entry/exit with correlationId
│   │   ├── ExceptionBehavior.cs       # Catch, log, suppress — never propagate to bus
│   │   ├── ValidationBehavior.cs      # Validate OID received is well-formed
│   │   └── OidResolutionBehavior.cs   # Lookup OID in map; enrich notification context
│   └── Handlers/
│       └── OtelMetricHandler.cs       # INotificationHandler: record gauge/counter/info
│
├── Telemetry/
│   ├── K8sLeaseElection.cs            # coordination.k8s.io/v1 Lease
│   ├── AlwaysLeaderElection.cs        # Local dev stub
│   ├── MetricRoleGatedExporter.cs     # Gate business metrics behind leadership
│   ├── SimetraLogEnrichmentProcessor.cs
│   └── TelemetryConstants.cs          # Meter names, source names
│
├── Scheduling/
│   ├── HeartbeatJob.cs                # Self-trap to prove pipeline alive
│   └── CorrelationJob.cs              # Rotate correlationId on schedule
│
├── HealthChecks/
│   ├── StartupHealthCheck.cs
│   ├── ReadinessHealthCheck.cs
│   └── LivenessHealthCheck.cs         # Staleness check via ILivenessVectorService
│
├── Configuration/
│   ├── OidMapOptions.cs               # flat Dictionary<string, OidMapEntry>
│   ├── DevicesOptions.cs
│   ├── OtlpOptions.cs
│   └── Validators/
│       └── ...
│
├── Extensions/
│   └── ServiceCollectionExtensions.cs  # Grouped AddX() methods, documented startup order
│
└── Lifecycle/
    └── GracefulShutdownService.cs      # Registered LAST; stops FIRST
```

### Structure Rationale

- **Ingestion/:** Separates intake (UDP trap reception, Quartz polling) from pipeline logic. Both converge on MediatR.Publish — the boundary is the notification.
- **Pipeline/Behaviors/:** MediatR behaviors execute in registration order. Grouping them here makes ordering visible and testable in isolation.
- **Pipeline/Handlers/:** Handlers are terminal. OtelMetricHandler is the single terminal handler for SnmpOidReceived.
- **Telemetry/:** All OTel wiring including role gating isolated from business logic. Leader election lives here because it is a telemetry-export concern.
- **Scheduling/:** Jobs that are not about SNMP extraction (heartbeat, correlation rotation) grouped separately from poll jobs.
- **Extensions/:** Single file documents the DI startup sequence in comments — critical for understanding shutdown order.
- **Lifecycle/:** GracefulShutdownService must be registered last. Isolating it prevents accidental re-ordering.

---

## Architectural Patterns

### Pattern 1: MediatR as Internal Event Bus

**What:** Both SnmpTrapListener and SnmpPoller publish `SnmpOidReceived` notifications. Neither knows about metrics, OID resolution, or logging. The pipeline behaviors and terminal handler are the only concern-aware code.

**When to use:** When multiple ingestion paths (trap vs. poll) must share identical processing logic. The notification is the unification point.

**Trade-offs:**
- Pro: Adding a new ingestion source (e.g., SNMP v3, REST webhook) requires only implementing the publisher side.
- Pro: Each behavior is independently unit-testable with a mock `next` delegate.
- Con: MediatR publish is fire-and-forget for INotification; there is no return value. Ensure behaviors do not need to return data up the chain.
- Con: Pipeline order is determined by DI registration order for behaviors — this must be documented and enforced.

**Example:**
```csharp
// Listener publishes — no knowledge of what happens next
await _mediator.Publish(new SnmpOidReceived(
    Oid: oid,
    Value: value,
    SenderIp: senderIp,
    ReceivedAt: DateTimeOffset.UtcNow), ct);

// Behavior intercepts in order
public sealed class LoggingBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : INotification
{
    public async Task Handle(TNotification notification,
        RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _logger.LogDebug("Handling {Type}", typeof(TNotification).Name);
        await next();
    }
}
```

### Pattern 2: BoundedChannel per Device (Trap Path Only)

**What:** SnmpTrapListener writes TrapEnvelopes to a per-device BoundedChannel (DropOldest). ChannelConsumerService spawns one Task per device that reads via ReadAllAsync. Poll jobs bypass this entirely (PIPE-06): they go directly to extraction and processing.

**When to use:** When traps are high-frequency bursts that may arrive faster than processing can consume. The channel decouples receipt rate from processing rate and provides backpressure.

**Trade-offs:**
- Pro: Trap receipt loop is never blocked by slow OID resolution or metric recording.
- Pro: Per-device isolation — a slow device does not delay other devices.
- Con: An in-memory channel means traps buffered at shutdown must be drained explicitly. GracefulShutdownService coordinates this: complete writers → wait for drain → flush telemetry.
- Con: DropOldest silently discards the oldest entries under sustained overload. Log the drop callback at Debug level to make this visible.

**Note for MediatR design:** The trap path publishes SnmpOidReceived per varbind from within the ChannelConsumerService (after channel read), not from the listener directly. The channel is an ingestion buffer; MediatR is the processing bus.

```csharp
// In ChannelConsumerService, after reading envelope from channel:
foreach (var varbind in envelope.Varbinds)
{
    await _mediator.Publish(new SnmpOidReceived(
        Oid: varbind.Id.ToString(),
        Value: varbind.Data.ToString(),
        SenderIp: envelope.SenderAddress,
        DeviceName: device.Name,
        CorrelationId: envelope.CorrelationId,
        ReceivedAt: envelope.ReceivedAt), ct);
}
```

### Pattern 3: MetricRoleGatedExporter for HA Multi-Pod Deployment

**What:** All pods run simultaneously. Only the leader exports business metrics (Simetra.Leader meter). All pods export pipeline/runtime metrics (Simetra.Instance meter and System.Runtime). MetricRoleGatedExporter wraps OtlpMetricExporter and filters by meter name per export cycle.

**When to use:** When running multiple replicas under K8s. Prevents duplicate metric time series from multiple pods while maintaining pod-level visibility for operational health.

**Trade-offs:**
- Pro: Near-instant failover — leader releases lease on SIGTERM; follower acquires within lease TTL.
- Pro: Pipeline metrics (trap received count, poll executed count) are visible on all pods regardless of role.
- Con: MetricRoleGatedExporter must propagate ParentProvider via reflection (internal setter on BaseExporter<Metric>). This is a brittleness point that should be tested.
- Con: Cannot use AddOtlpExporter() convenience method because it constructs the exporter internally, preventing wrapping. Manual OtlpMetricExporter construction is required.

### Pattern 4: Dual Correlation ID (Global + Per-Operation AsyncLocal)

**What:** RotatingCorrelationService holds two IDs:
1. `CurrentCorrelationId` — volatile string, rotated by CorrelationJob on schedule. Represents the current "epoch" or work interval.
2. `OperationCorrelationId` — AsyncLocal<string?>, set for the duration of a single job execution or trap processing. Cleared in `finally`. Used by SimetraLogEnrichmentProcessor.

**When to use:** When you need both "which interval did this belong to" (global) and "which specific operation generated this log line" (per-operation).

**Trade-offs:**
- Pro: Log lines during a job execution are tagged with the operation-scoped correlationId, which is the global ID captured at job start. This ties log lines to a specific scheduling interval.
- Pro: CorrelationId rotating on schedule (not per-request) keeps cardinality low in OTLP log backends.
- Con: AsyncLocal leaks if OperationCorrelationId is not cleared in finally. The pattern is: set in try, clear in finally.

### Pattern 5: OID Map as Flat Dictionary in appsettings

**What:** OID-to-metadata mapping is a `Dictionary<string, OidMapEntry>` in appsettings.json, not a database. OidResolutionBehavior looks up the OID string key and enriches the MediatR notification context with the resolved entry.

**When to use:** When OID sets are static, known at deployment time, and small enough to live in configuration (hundreds, not thousands).

**Trade-offs:**
- Pro: Zero runtime dependency — no database read on the hot path.
- Pro: Change an OID mapping by updating appsettings and restarting.
- Con: No hot-reload without process restart (unless IOptionsMonitor is used, which adds complexity).
- Con: Grows unwieldy beyond ~500 OIDs. At that scale, consider a SQLite/EF Core lookup with startup-time loading into a ConcurrentDictionary.

---

## Data Flow

### Trap Path (SnmpTrapListener → Metrics)

```
Network device
    │  SNMP v2c trap UDP packet
    ↓
SnmpListenerService (BackgroundService)
    │  MessageFactory.ParseMessages()
    │  community check
    │  listener middleware pipeline:
    │    ErrorHandlingMiddleware (outermost)
    │    CorrelationIdMiddleware (stamps correlationId onto envelope)
    │    LoggingMiddleware
    │  DeviceRegistry.TryGetDevice(senderIp) → DeviceInfo
    │  TrapFilter.Match(varbinds, device) → PollDefinitionDto
    ↓
DeviceChannelManager.GetWriter(deviceName).WriteAsync(envelope)
    ↓
BoundedChannel<TrapEnvelope> [per device, DropOldest]
    ↓
ChannelConsumerService (BackgroundService, one Task per device)
    │  ReadAllAsync()
    │  consumer middleware pipeline (error handling + logging)
    │  per varbind in envelope.Varbinds:
    │    MediatR.Publish(SnmpOidReceived)
    │        ↓ LoggingBehavior
    │        ↓ ExceptionBehavior
    │        ↓ ValidationBehavior
    │        ↓ OidResolutionBehavior (lookup flat Dictionary)
    │        ↓ OtelMetricHandler
    │              MetricFactory.RecordMetrics()
    │                  → Gauge<double>.Record() or Counter<double>.Add()
    │                  labels: site_name, device_name, device_ip, device_type, + static + dynamic
    ↓
System.Diagnostics.Metrics instruments (Simetra.Leader meter)
    ↓
MetricRoleGatedExporter
    ├── leader: export all meters via OtlpMetricExporter
    └── follower: export only Simetra.Instance + System.Runtime
    ↓
OTLP Collector → Prometheus → Grafana
```

### Poll Path (Quartz → Metrics, bypasses channels)

```
Quartz scheduler fires MetricPollJob or StatePollJob
    │  read correlationId (SCHED-08: capture BEFORE execution, scope as OperationCorrelationId)
    │  DeviceRegistry.TryGetDeviceByName()
    │  PollDefinitionRegistry.TryGetDefinition()
    │  Messenger.GetAsync() [SharpSnmpLib SNMP GET, with 80% interval timeout]
    ↓
ISnmpExtractor.Extract(response, definition)
    → ExtractionResult { Metrics, Labels, Definition }
    ↓
ProcessingCoordinator.Process(result, device, correlationId)  [or MediatR.Publish]
    ├── Branch A: MetricFactory.RecordMetrics() [always]
    └── Branch B: StateVectorService.Update() [Source=Module only]
    ↓
[same OTel export path as trap path]
    ↓
finally: ILivenessVectorService.Stamp(jobKey) [always, even on failure]
```

### Telemetry Export Flow

```
System.Diagnostics.Metrics instruments
    ↓
PeriodicExportingMetricReader (automatic, configured interval)
    ↓
MetricRoleGatedExporter.Export(batch)
    ├── if leader: pass all metrics to OtlpMetricExporter
    └── if follower: filter out Simetra.Leader metrics, pass remainder

OtlpMetricExporter → OTLP endpoint (e.g., otel-collector:4317)
    ↓
Prometheus scrape / push → Grafana
```

### Shutdown Sequence (GracefulShutdownService, registered last → stops first)

```
SIGTERM received
    ↓
GracefulShutdownService.StopAsync() [5 time-budgeted steps]
    │
    ├── Step 1 (3s budget): K8sLeaseElection.StopAsync()
    │     → DELETE lease → followers acquire immediately
    │
    ├── Step 2 (3s budget): SnmpListenerService.StopAsync()
    │     → UDP socket closed → no new traps accepted
    │
    ├── Step 3 (3s budget): IScheduler.Standby()
    │     → no new job fires, in-flight jobs complete
    │
    ├── Step 4 (8s budget): DeviceChannelManager.CompleteAll() + WaitForDrainAsync()
    │     → writers completed → ChannelConsumerService Tasks finish naturally
    │
    └── Step 5 (5s budget, own CTS — always runs): ForceFlush MeterProvider + TracerProvider
          → buffered metrics/traces sent before process exits
```

---

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 1-5 pods | Single Deployment with K8s Lease; one leader exports business metrics. Current design handles this natively. |
| 5-20 pods | Increase Quartz thread pool sizing (auto-scaled by job count already). Review BoundedChannel capacity if trap burst rates are high. |
| 20+ pods | Consider partitioning devices across pod groups (each pod group responsible for a subnet). Leader election per group. OID map may need migration from appsettings to shared config source. |
| 100+ devices | If OID map grows large (>500 entries), migrate from flat Dictionary in appsettings to startup-loaded ConcurrentDictionary from SQLite. |

### Scaling Priorities

1. **First bottleneck — channel backpressure under sustained trap storm:** BoundedChannel with DropOldest silently drops oldest entries. Surface this via `snmp_trap_dropped` counter. Consider increasing capacity or adding a secondary processing pod.
2. **Second bottleneck — Quartz thread pool exhaustion:** Default thread pool is sized to job count at startup. If SNMP GET timeouts accumulate, threads back up. `[DisallowConcurrentExecution]` prevents overlap per job key but does not prevent total thread exhaustion across all jobs. Monitor `quartz_thread_pool_active` if using Quartz OTel metrics.
3. **Third bottleneck — OTLP export throughput:** PeriodicExportingMetricReader batches metrics on its own interval. If metric cardinality (unique label combinations) explodes (e.g., per-OID gauges × devices × sites), Prometheus scrape times and memory grow. Keep label cardinality bounded — avoid raw OID strings as label values.

---

## Anti-Patterns

### Anti-Pattern 1: Publishing SnmpOidReceived from SnmpListenerService Directly

**What people do:** Publish the MediatR notification from within the trap receive loop, before the channel.

**Why it's wrong:** The UDP receive loop is single-threaded. MediatR.Publish is awaited synchronously from the loop perspective. If OtelMetricHandler or any behavior is slow, the listener falls behind. Traps are lost (UDP has no acknowledgment). The channel exists specifically to decouple receipt rate from processing rate.

**Do this instead:** Write to the channel in the listener. Publish MediatR notification from ChannelConsumerService after reading from the channel.

### Anti-Pattern 2: Registering Two Instances of K8sLeaseElection

**What people do:** Register `services.AddSingleton<ILeaderElection, K8sLeaseElection>()` and then separately `services.AddHostedService<K8sLeaseElection>()`.

**Why it's wrong:** .NET DI creates TWO separate singleton instances — one for ILeaderElection consumers, one for the hosted service. The hosted service updates its internal `_isLeader` flag but consumers read from a different instance that never gets updated. All pods appear to be non-leaders from the exporter's perspective.

**Do this instead:** Register the concrete type first, then resolve for both registrations:
```csharp
services.AddSingleton<K8sLeaseElection>();
services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<K8sLeaseElection>());
services.AddHostedService(sp => sp.GetRequiredService<K8sLeaseElection>());
```

### Anti-Pattern 3: GracefulShutdownService Not Registered Last

**What people do:** Register GracefulShutdownService early (e.g., right after telemetry) for convenience.

**Why it's wrong:** .NET Generic Host stops IHostedService instances in reverse registration order. If GracefulShutdownService is not the last registered, it will not be the first to stop. The shutdown sequence (lease release → listener stop → scheduler standby → channel drain → flush) depends on it running before any other hosted service stops.

**Do this instead:** Always call `services.AddSimetraLifecycle()` (which registers GracefulShutdownService) as the last DI registration call. Document this constraint prominently in the extension method's XML doc.

### Anti-Pattern 4: OtelMetricHandler Creating Instruments on Every Invocation

**What people do:** Call `_meter.CreateGauge<double>(metricName)` inside the handler for each notification.

**Why it's wrong:** System.Diagnostics.Metrics instruments should be created once and reused. Creating repeatedly (a) causes warnings/errors if the same meter is used, (b) adds unnecessary allocation on the hot path.

**Do this instead:** Use ConcurrentDictionary as an instrument cache in MetricFactory (or equivalent), keyed by metric name. GetOrAdd creates on first call, returns existing on subsequent calls.

### Anti-Pattern 5: Missing ExceptionBehavior Allows Exceptions to Propagate to MediatR Bus

**What people do:** Rely on the outer behavior or handler to catch exceptions, or assume MediatR handles them.

**Why it's wrong:** An uncaught exception from any behavior or handler will propagate up to the publisher (ChannelConsumerService). In the consumer loop, this will log the error and skip the envelope — acceptable — but if the exception escapes the loop's try/catch, the consumer Task faults and the device channel stops being consumed permanently for that device's lifetime.

**Do this instead:** ExceptionBehavior wraps `next()` in try/catch, logs the exception, and returns without re-throwing. Pipeline continues to next iteration. The handler should never fault the consumer Task.

### Anti-Pattern 6: OperationCorrelationId AsyncLocal Not Cleared in Finally

**What people do:** Set `_correlation.OperationCorrelationId = correlationId` at the start of a Quartz job but only clear it in the happy path.

**Why it's wrong:** AsyncLocal values flow with the async context. If the job throws and the finally is missing, the thread returned to the Quartz thread pool still carries the stale operation ID. The next job on that thread inherits it, causing log lines from the next job to appear correlated to the previous job's operation.

**Do this instead:** Always clear in finally:
```csharp
try
{
    _correlation.OperationCorrelationId = correlationId;
    // ... job work ...
}
finally
{
    _correlation.OperationCorrelationId = null;
    _liveness.Stamp(jobKey);
}
```

---

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| Network SNMP devices | UDP:162 inbound trap receive (SharpSnmpLib); SNMP GET outbound poll (SharpSnmpLib Messenger.GetAsync) | Community string auth only (v2c). Trap source IP must match DeviceRegistry entry. |
| OTLP Collector | OtlpMetricExporter + OtlpTraceExporter (gRPC, port 4317) + OTLP log exporter | All three use same endpoint. MetricRoleGatedExporter gates business metrics. Logs not gated. |
| Prometheus / Grafana | OTel → OTLP Collector → Prometheus remote write or Prometheus scrape of OTLP collector | No direct Prometheus client in the agent. Push-only model via OTLP. |
| Kubernetes API | coordination.k8s.io/v1 Lease via KubernetesClient (k8s dotnet SDK) | In-cluster config auto-detected via KubernetesClientConfiguration.IsInCluster(). Local dev falls through to AlwaysLeaderElection. |
| Quartz.NET scheduler | In-memory store (no DB). QuartzHostedService manages lifecycle. | Thread pool auto-sized to job count at startup. WaitForJobsToComplete=true for graceful shutdown. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Ingestion → Pipeline | MediatR INotification publish | SnmpOidReceived is the contract. Both trap and poll paths publish the same type. |
| SnmpListenerService → ChannelConsumerService | BoundedChannel<TrapEnvelope> | Only for trap path. Per-device. DropOldest under load. |
| ProcessingCoordinator → MetricFactory | Direct method call (Branch A, always) | Synchronous. Not async. Instrument recording is not awaitable. |
| ProcessingCoordinator → StateVectorService | Direct method call (Branch B, Module source only) | ConcurrentDictionary update. Not async. |
| Any job → ICorrelationService | volatile string read (global) + AsyncLocal set/clear (per-operation) | Single writer (CorrelationJob). Multiple readers. Volatile ensures visibility without lock. |
| OtelMetricHandler → MetricFactory | Direct method call or can be the same class | Instrument creation cached in ConcurrentDictionary. |
| GracefulShutdownService → All shutdown targets | Direct method calls (StopAsync, Standby, CompleteAll, ForceFlush) | Each step has its own CancellationTokenSource with time budget. Step 5 (flush) has independent CTS — not linked to outer shutdown token. |

---

## Build Order Implications for Phases

The architecture has clear dependency layers that dictate phase ordering:

**Phase 1 — Infrastructure Foundation**
Must exist before anything else can run. Includes: DI host setup, configuration loading with ValidateOnStart, OTel provider registration, leader election skeleton.
- No component depends on this; everything depends on it.

**Phase 2 — Device Registry + OID Map**
DeviceRegistry (IP → DeviceInfo) and OID flat Dictionary must exist before ingestion. Without them, trap receipt and poll resolution have nothing to look up.
- Depends on: Phase 1 (configuration, DI)

**Phase 3 — MediatR Pipeline (Behaviors + Handler)**
The behaviors and OtelMetricHandler can be built and unit-tested in isolation before the ingestion layer exists. Use MediatR.Publish directly in tests.
- Depends on: Phase 1 (DI), Phase 2 (OID map, for OidResolutionBehavior)

**Phase 4 — Ingestion (Trap Listener + Channels)**
SnmpTrapListener + DeviceChannelManager + ChannelConsumerService. The consumer publishes to MediatR.
- Depends on: Phase 1, Phase 2, Phase 3

**Phase 5 — Quartz Scheduling (Poll Jobs)**
MetricPollJob and StatePollJob. These bypass channels and publish directly to MediatR.
- Depends on: Phase 1, Phase 2, Phase 3 (MediatR pipeline must exist to receive publications)

**Phase 6 — Auxiliary Jobs (Heartbeat, Correlation)**
HeartbeatJob (proves pipeline alive), CorrelationJob (rotates global ID). These depend on the SNMP trap path being complete (heartbeat sends a trap to self, which must be received and processed).
- Depends on: Phase 4 (trap path must be functional for heartbeat loopback)

**Phase 7 — Graceful Shutdown + Health Probes**
GracefulShutdownService (5-step sequence), LivenessHealthCheck (staleness-based), StartupHealthCheck, ReadinessHealthCheck.
- Depends on: All prior phases (shuts down all components)

---

## Sources

- Reference implementation: `src/Simetra/` (read directly — HIGH confidence)
  - `Program.cs` — DI registration order, 11-step startup sequence
  - `Extensions/ServiceCollectionExtensions.cs` — Full 11-step + 5-step shutdown documentation
  - `Services/SnmpListenerService.cs` — Trap intake pattern with middleware pipeline
  - `Services/ChannelConsumerService.cs` — Channel consumer, consumer-side pipeline
  - `Pipeline/DeviceChannelManager.cs` — BoundedChannel per device, DropOldest
  - `Pipeline/DeviceRegistry.cs` — IP + name lookup, module attachment
  - `Pipeline/ProcessingCoordinator.cs` — Dual-branch processing
  - `Pipeline/MetricFactory.cs` — Instrument cache, label assembly
  - `Pipeline/StateVectorService.cs` — ConcurrentDictionary state store
  - `Pipeline/RotatingCorrelationService.cs` — volatile global + AsyncLocal per-operation
  - `Pipeline/TrapPipelineBuilder.cs` — Middleware composition pattern
  - `Telemetry/K8sLeaseElection.cs` — Lease API, near-instant failover on SIGTERM
  - `Telemetry/MetricRoleGatedExporter.cs` — Meter-name-based gating, ParentProvider propagation
  - `Jobs/MetricPollJob.cs` — Poll-bypass-channel pattern, SCHED-08 correlation, liveness stamp
  - `Lifecycle/GracefulShutdownService.cs` — 5-step shutdown, per-step CTS budgets
  - `HealthChecks/LivenessHealthCheck.cs` — Staleness check against IJobIntervalRegistry

---
*Architecture research for: SNMP monitoring system — C# .NET 9, MediatR, OTel push*
*Researched: 2026-03-04*
