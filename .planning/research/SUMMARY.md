# Project Research Summary

**Project:** Simetra117 — SNMP Monitoring System
**Domain:** Network device telemetry collection, C# .NET 9, OTel push pipeline to Prometheus/Grafana
**Researched:** 2026-03-04
**Confidence:** HIGH (stack and architecture derived from reference implementation; pitfalls sourced from RFCs and official docs)

## Executive Summary

This is a K8s-native SNMP monitoring agent that collects device telemetry via two paths (trap reception on UDP 162 and scheduled SNMP GET/GETBULK polling) and exports metrics to Prometheus/Grafana via an OpenTelemetry push pipeline. The reference implementation at `src/Simetra/` already establishes the full architectural pattern — this project is a disciplined re-implementation and extension of that pattern, not greenfield design. The stack is definitive: Lextm.SharpSnmpLib 12.5.7 for SNMP protocol work, Quartz.NET 3.16.0 for poll scheduling, MediatR 12.5.0 (MIT) as the internal event bus, OpenTelemetry 1.15.0 for telemetry export, and KubernetesClient 19.0.2 for leader election via K8s Lease API.

The recommended architecture separates ingestion (trap listener + Quartz poller) from processing (MediatR pipeline with behaviors) from export (role-gated OTLP push). Both ingestion paths converge on a single `SnmpOidReceived` MediatR notification, allowing behaviors (logging, exception handling, OID resolution) to apply uniformly regardless of source. Traps are buffered through per-device `BoundedChannel<TrapEnvelope>` instances to decouple reception rate from processing rate. Poll jobs bypass channels entirely and publish directly to MediatR. Only the leader pod exports business metrics; all pods export pipeline and runtime metrics. Graceful shutdown is an orchestrated 5-step sequence registered as the last hosted service.

The critical risks are front-loaded: OTel metric cardinality must be designed before any instruments are created (retroactive migration of Prometheus series is HIGH cost), counter delta logic (wrap-around and reboot detection) must be correct before any counter metrics reach dashboards (bad historical data cannot be corrected retroactively), and the SharpSnmpLib ObjectStore thread-safety constraint must be avoided by routing traps through channels rather than the ObjectStore directly. The MediatR notification publisher strategy (default ForeachAwaitPublisher vs TaskWhenAllPublisher) must also be decided at pipeline design time to prevent silent metric drops on handler failures.

---

## Key Findings

### Recommended Stack

All stack choices are verified against NuGet and official sources as of 2026-03-04. The stack is stable and appropriate for .NET 9. The only decision requiring a judgment call is MediatR version: use 12.5.0 (MIT, last fully free version) unless the team is already licensed for commercial MediatR use. The pipeline behaviors pattern MediatR enables is valuable but replaceable (~200 lines) if licensing is a blocker.

All OTel packages (SDK, OTLP Exporter, Extensions.Hosting) must be pinned to the same version (1.15.0) — mixed versions cause runtime errors.

**Core technologies:**
- **Lextm.SharpSnmpLib 12.5.7:** SNMP protocol — trap reception and polling — the only actively maintained MIT-licensed SNMP library for modern .NET
- **Quartz.NET 3.16.0:** Poll scheduling with per-job cron/interval config, misfire handling, and [DisallowConcurrentExecution] — no database required (RAM scheduler)
- **MediatR 12.5.0 (MIT):** Internal event bus and pipeline behavior chain — use v12.5.0 to avoid RPL-1.5 license exposure on v13+
- **OpenTelemetry 1.15.0 (SDK + OTLP Exporter + Extensions.Hosting):** Metrics, logs, and traces via gRPC push to OTel Collector — all same version required
- **KubernetesClient 19.0.2:** K8s Lease API for leader election — Apache-2.0, includes LeaderElector with LeaseLock

**Key stack decisions validated:**
- `snmp_info` implemented as `ObservableGauge<long>` (always value=1) with device attributes — matches Prometheus `target_info` convention
- `snmp_gauge` as `ObservableGauge<double>`, `snmp_counter` as `ObservableCounter<long>` — correct TypeCode-to-instrument mapping
- Push pipeline (App → OTLP gRPC → OTel Collector → `prometheusremotewriteexporter` → Prometheus) is the established pattern; do not mix with prometheus-net scrape endpoint

### Expected Features

The MVP is well-defined. All P1 features are already represented in the reference architecture. The scope boundary is clear: this system is a collection and export agent, not an alerting engine, MIB browser, topology discovery tool, or TSDB. Staying narrow is a design strength.

**Must have (v1 table stakes):**
- SNMP trap reception (UDP 162) with backpressure via bounded channels
- Scheduled SNMP GET/GETBULK polling via Quartz, configurable per device
- OID-to-metric-name resolution via flat Dictionary loaded from config
- Three typed OTel instruments: `snmp_gauge`, `snmp_counter`, `snmp_info` (TypeCode-based selection)
- OTel push to Prometheus via OTLP — the delivery mechanism for all metrics
- Pipeline health metrics: `traps_received`, `traps_dropped`, `poll_success`, `poll_failure`, `oid_miss_rate`
- Structured logging with correlation IDs propagated through all processing paths
- K8s leader election — prevents duplicate metric export across pod replicas
- Graceful handling of unreachable devices — circuit-breaker/backoff to avoid log flooding and poll queue blocking

**Should have (v1.x after validation):**
- Hot-reloadable OID map (IOptionsMonitor or FileSystemWatcher) — needed after first firmware-update incident
- Per-device polling intervals — Quartz job config per target is already supported
- Trap storm protection / per-source-IP rate limiting — needed before first noisy device incident
- SNMP v3 auth + encryption — SharpSnmpLib already supports it; config path is the work
- SNMP Inform acknowledgment support — for higher-reliability trap delivery

**Defer to v2+:**
- OID walk / auto-discovery of device OIDs
- Grafana dashboard provisioning (JSON/ConfigMap)
- SNMP SET / configuration push (requires separate security design)
- Built-in alerting engine, MIB browser, network topology discovery — all anti-features that duplicate existing tooling

### Architecture Approach

The architecture follows a layered ingestion-pipeline-export model derived directly from the reference implementation. The key insight is that both trap and poll paths converge on a single MediatR notification type (`SnmpOidReceived`), so all cross-cutting concerns (logging, exception handling, OID resolution) are implemented once in behaviors rather than duplicated across ingestion paths. Role-gated metric export ensures multi-replica K8s deployments produce exactly one set of business metric series regardless of pod count.

**Major components:**
1. **SnmpTrapListener (BackgroundService)** — binds UDP:162, runs listener middleware pipeline, routes per-device to BoundedChannel; never publishes to MediatR directly
2. **DeviceChannelManager** — one BoundedChannel<TrapEnvelope> per device, DropOldest under load; decouples receipt rate from processing rate
3. **ChannelConsumerService (BackgroundService)** — one Task per device channel; reads envelopes and publishes SnmpOidReceived per varbind to MediatR
4. **MetricPollJob / StatePollJob (Quartz IJob)** — SNMP GET at configured intervals; bypasses channels; publishes SnmpOidReceived directly to MediatR
5. **MediatR Behavior Pipeline** — LoggingBehavior → ExceptionBehavior → ValidationBehavior → OidResolutionBehavior → OtelMetricHandler (terminal)
6. **MetricFactory (Singleton)** — instrument cache via ConcurrentDictionary; records snmp_gauge/snmp_counter/snmp_info with assembled label sets
7. **K8sLeaseElection (BackgroundService + ILeaderElection)** — acquires coordination.k8s.io/v1 Lease; exposes volatile `IsLeader`; falls back to AlwaysLeaderElection outside K8s
8. **MetricRoleGatedExporter** — wraps OtlpMetricExporter; passes Simetra.Leader meter only when leader; passes Instance + Runtime meters always
9. **GracefulShutdownService (registered last, stops first)** — 5-step orchestrated shutdown with per-step CancellationTokenSource budgets
10. **RotatingCorrelationService** — volatile global correlationId (epoch-scoped) + AsyncLocal operation-scoped ID; cleared in finally by all Quartz jobs

### Critical Pitfalls

These pitfalls have HIGH recovery cost or are non-obvious implementation traps. Address them in the phase where they are introduced, not after.

1. **OTel cardinality explosion** — design label taxonomy before creating any instruments; never use raw OID strings, IP addresses, or request IDs as label values; set `CardinalityLimit` via OTel View API; OTel SDK silently drops data above 2,000 unique series per instrument; Prometheus series migration after the fact is HIGH cost
2. **Counter32/Counter64 wrap-around misread as traffic spike** — implement wrap-aware delta formula before any counter metrics reach dashboards; also poll `sysUpTime` every cycle to detect device reboots (which reset counters to zero and are indistinguishable from wrap-around); bad historical data in Prometheus cannot be corrected retroactively
3. **MediatR ForeachAwaitPublisher fails fast on first handler exception** — the default notification publisher stops at the first failing handler; if OtelMetricHandler runs after a failing handler, metrics are silently dropped; switch to TaskWhenAllPublisher or custom per-handler catch wrapping at pipeline design time
4. **SharpSnmpLib ObjectStore not thread-safe** — `SnmpEngine` dispatches to CLR thread pool; concurrent trap handling writes to ObjectStore cause data corruption under load; avoid ObjectStore entirely for trap listener use cases and route to per-device channels instead (already in design)
5. **K8sLeaseElection dual-registration DI pitfall** — registering as both `ILeaderElection` singleton and `IHostedService` creates two separate instances; the hosted service updates its own `_isLeader` flag while exporters read from a different instance that never changes; always register the concrete type first and resolve both registrations from the same singleton instance
6. **GracefulShutdownService registration order** — .NET Generic Host stops hosted services in reverse registration order; GracefulShutdownService must be registered last to stop first; the 5-step shutdown sequence (lease release → listener stop → scheduler standby → channel drain → telemetry flush) depends on this

---

## Implications for Roadmap

The architecture research provides an explicit build-order dependency graph with 7 layers. These map directly to phases. The cardinality pitfall and counter delta pitfall both require design work before implementation begins in their respective phases — do not defer design decisions to "we'll figure it out when it breaks."

### Phase 1: Infrastructure Foundation

**Rationale:** Every other component depends on this layer. DI host setup, configuration loading with ValidateOnStart, OTel provider registration (all three signal types), and leader election skeleton must exist before any ingestion or pipeline work begins. No component can be tested without a host.
**Delivers:** Running .NET 9 Generic Host with OTel SDK wired, IConfiguration loaded and validated, AlwaysLeaderElection stub for local dev, OTLP exporter configured (endpoint only, no metrics yet), structured logging pipeline active
**Addresses features:** Configuration via environment/config file, structured logging foundation
**Avoids pitfall:** OTel version mismatch (all three OTel packages at 1.15.0 from day 1); GracefulShutdownService registered as last hosted service from day 1

### Phase 2: Device Registry and OID Map

**Rationale:** Both ingestion paths (trap listener and Quartz poller) require DeviceRegistry (IP → DeviceInfo) and OID flat Dictionary before they can resolve anything. Building and validating these lookup structures before wiring ingestion ensures OID resolution is correct and cardinality-bounded before first metrics are emitted.
**Delivers:** DeviceRegistry singleton populated from DevicesOptions; OID Dictionary loaded from appsettings; OidResolutionBehavior unit-testable in isolation; label taxonomy documented and cardinality-counted against target device fleet
**Addresses features:** OID-to-human-readable metric name resolution, device-agnostic operation
**Avoids pitfall:** Cardinality explosion — label set designed and verified against device count before any instruments are created; linear OID lookup replaced with O(1) Dictionary from the start

### Phase 3: MediatR Pipeline (Behaviors and Handler)

**Rationale:** The MediatR pipeline is the central processing contract. Both ingestion paths publish to it. Build and unit-test all behaviors and OtelMetricHandler before wiring any ingestion — this makes the terminal handler independently verifiable with mock notifications.
**Delivers:** LoggingBehavior, ExceptionBehavior, ValidationBehavior, OidResolutionBehavior (using Phase 2 registry), OtelMetricHandler with MetricFactory instrument cache; notification publisher strategy decided (TaskWhenAllPublisher or custom per-handler catch); snmp_gauge/snmp_counter/snmp_info instruments created and verified via dotnet-counters
**Addresses features:** Three metric instruments (TypeCode-based), pipeline health metrics foundation
**Avoids pitfall:** MediatR ForeachAwaitPublisher fail-fast — publisher strategy set before any multi-handler integration; MetricFactory instrument creation cached (not per-invocation)

### Phase 4: Trap Ingestion (Listener, Channels, Consumer)

**Rationale:** Trap ingestion is the primary async event path. SnmpTrapListener, DeviceChannelManager, and ChannelConsumerService must be built together — they form a single flow and cannot be tested in isolation without each other. The anti-pattern (publishing from the listener directly) must be explicitly avoided here.
**Delivers:** UDP:162 listener receiving traps; per-device BoundedChannel with DropOldest; ChannelConsumerService publishing SnmpOidReceived per varbind; listener middleware pipeline (error handling, correlation ID stamping, logging); traps_received and traps_dropped counters active
**Addresses features:** SNMP trap reception, graceful handling of unreachable devices, pipeline health metrics
**Avoids pitfall:** SharpSnmpLib ObjectStore thread safety (avoided by design — no ObjectStore use); UDP listener never publishes to MediatR directly (anti-pattern 1); SNMPv3 time window rejection logging wired even if v3 not yet enabled

### Phase 5: Poll Ingestion (Quartz Scheduling)

**Rationale:** Poll jobs are the baseline collection path for devices that don't send traps for all state changes. They depend on Phase 3 (MediatR pipeline must exist to receive publications) and Phase 2 (DeviceRegistry and PollDefinitionRegistry). Poll jobs bypass channels (they publish directly to MediatR) so they can be built and tested against the Phase 3 pipeline without Phase 4.
**Delivers:** MetricPollJob and StatePollJob (Quartz IJob, [DisallowConcurrentExecution]); per-device SNMP GET with 80%-of-interval timeout; ProcessingCoordinator dual-branch (metrics always; StateVector for Module source only); SCHED-08 correlation ID capture before execution; liveness stamp in finally block
**Addresses features:** Scheduled SNMP GET/GETBULK polling, per-device graceful unreachability handling (error budget, backoff)
**Avoids pitfall:** Quartz thread pool starvation — pool sized relative to device count × poll frequency × SNMP timeout from configuration; SNMP poll wrapped in CancellationToken with timeout; AsyncLocal OperationCorrelationId cleared in finally

### Phase 6: Telemetry Export and Leader Election

**Rationale:** Role-gated export requires a working pipeline (Phases 3-5) to have metrics to gate. K8sLeaseElection and MetricRoleGatedExporter can be built in parallel with Phases 4-5 but must be integrated and verified with a two-instance test before any production deployment.
**Delivers:** K8sLeaseElection (coordination.k8s.io/v1 Lease); AlwaysLeaderElection fallback for local dev; MetricRoleGatedExporter (Simetra.Leader gated, Instance + Runtime always); SimetraLogEnrichmentProcessor (site/role/correlationId on all log lines); two-instance test confirming exactly one set of business metric series in Prometheus
**Addresses features:** K8s leader election, pipeline health as first-class metrics, leader-based business metric export
**Avoids pitfall:** K8sLeaseElection dual-registration DI pitfall — register concrete type once, resolve both interfaces from same singleton; duplicate metric series from multiple instances

### Phase 7: Auxiliary Jobs, Graceful Shutdown, and Health Probes

**Rationale:** Heartbeat and correlation rotation depend on the full trap path being functional (HeartbeatJob sends a trap to 127.0.0.1 which must traverse the complete pipeline). Graceful shutdown must be verified end-to-end with an actual SIGTERM under load. Health probes require all components to be running to assert staleness correctly.
**Delivers:** HeartbeatJob (self-trap proving pipeline alive); CorrelationJob (rotating global correlationId); GracefulShutdownService (5-step: lease release → listener stop → scheduler standby → channel drain → telemetry flush); LivenessHealthCheck (staleness-based via ILivenessVectorService); StartupHealthCheck and ReadinessHealthCheck; OTel ForceFlush verified on shutdown
**Addresses features:** System self-health metrics, structured logging with correlation IDs (rotation)
**Avoids pitfall:** MeterProvider disposed without flush — ForceFlush explicitly called in Step 5 with independent CTS; Quartz job cancellation verified on shutdown (no hung threads)

### Phase Ordering Rationale

- Phases 1-2 are pure prerequisites: nothing can run without a host, and nothing can resolve without the registry and OID map.
- Phase 3 is intentionally decoupled from ingestion: the MediatR pipeline can be built and fully unit-tested using mock notifications before any network I/O exists.
- Phases 4 and 5 can proceed in parallel after Phase 3 — they share no direct dependency on each other, only on Phases 1-3.
- Phase 6 can begin in parallel with Phases 4-5 but integration (two-instance test) must follow Phase 4-5 completion.
- Phase 7 requires Phase 4 completion (heartbeat loopback) and naturally follows all other phases.
- The cardinality pitfall is addressed in Phase 2 (before instruments exist). The counter delta pitfall must be addressed in Phase 5 (before counter metrics reach Prometheus). Both have HIGH retroactive recovery cost — they cannot be deferred.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 5 (Poll Ingestion):** Counter delta engine (wrap-around + sysUpTime reboot detection) is moderately complex and has no standard .NET library — requires explicit implementation design before coding; also verify Quartz thread pool sizing formula against expected device count
- **Phase 6 (Leader Election):** MetricRoleGatedExporter uses reflection to propagate ParentProvider (internal setter on BaseExporter<Metric>) — this brittleness point needs verification against OTel 1.15.0 internals and a test that detects if the API changes

Phases with established patterns (skip research-phase):
- **Phase 1 (Infrastructure Foundation):** .NET Generic Host + OTel SDK registration is well-documented with official examples
- **Phase 3 (MediatR Pipeline):** MediatR behavior pipeline pattern is thoroughly documented; the specific behaviors (logging, exception, validation) are standard
- **Phase 4 (Trap Ingestion):** BoundedChannel per device with DropOldest and BackgroundService consumer is a standard .NET channels pattern; SharpSnmpLib listener setup is documented
- **Phase 7 (Graceful Shutdown):** IHostedService shutdown ordering is well-documented .NET behavior; the 5-step sequence is fully specified in the reference implementation

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All packages verified on NuGet with version, TFM, and license as of 2026-03-04; OTel push pipeline cross-referenced with OTel official docs |
| Features | MEDIUM | Table stakes sourced from multiple industry surveys (consistent signal); differentiator and anti-feature analysis is domain reasoning with MEDIUM confidence; MVP definition is strong |
| Architecture | HIGH | Derived directly from reference implementation at src/Simetra/ — this is not inferred, it is read from source; patterns confirmed against .NET and OTel docs |
| Pitfalls | HIGH | Counter wrap-around sourced from Cisco RFC and RFC1155; OTel cardinality from OTel official docs; MediatR publisher pitfall from library changelog; Quartz pitfalls from official best-practices docs |

**Overall confidence:** HIGH

### Gaps to Address

- **MediatR publisher strategy for parallel handlers:** TaskWhenAllPublisher runs handlers concurrently and shares the DI scope — if any handler uses a non-thread-safe service (EF Core DbContext), this causes race conditions. Audit all handler dependencies before enabling parallel dispatch. If handlers are all stateless/concurrent-safe, TaskWhenAllPublisher is the right choice; otherwise implement per-handler try/catch in a custom sequential publisher.
- **MetricRoleGatedExporter ParentProvider reflection:** Uses reflection to set an internal property on BaseExporter<Metric>. This works in OTel 1.15.0 but is fragile across OTel SDK version upgrades. Consider filing a feature request with the OTel .NET team for a supported API, or pinning the OTel version strictly and adding an integration test that detects breakage.
- **Counter delta engine design:** The wrap-around detection formula and sysUpTime-based reboot detection need explicit unit tests with synthetic counter values before any counter metrics reach production. The "looks done but isn't" checklist in PITFALLS.md provides the exact test cases.
- **SNMPv3 scope:** The reference implementation handles v2c only. If the target device fleet includes v3-only devices, the auth/privacy config path needs design work in Phase 4 or 5 before device onboarding. SharpSnmpLib supports v3 natively — no library change required, but EngineID management and key exchange add complexity.

---

## Sources

### Primary (HIGH confidence)
- `src/Simetra/` reference implementation (read directly) — architecture, patterns, component responsibilities, anti-patterns
- [NuGet: Lextm.SharpSnmpLib 12.5.7](https://www.nuget.org/packages/Lextm.SharpSnmpLib) — version and TFM verified
- [NuGet: Quartz 3.16.0](https://www.nuget.org/packages/Quartz) — version, .NET 9 target confirmed
- [NuGet: OpenTelemetry 1.15.0](https://www.nuget.org/packages/OpenTelemetry) — stable, net9.0 target
- [NuGet: KubernetesClient 19.0.2](https://www.nuget.org/packages/KubernetesClient) — Apache-2.0, net9.0 target
- [OTel .NET Metrics Best Practices](https://opentelemetry.io/docs/languages/dotnet/metrics/best-practices/) — cardinality limits, instrument creation patterns, info metric pattern
- [RFC 3414](https://datatracker.ietf.org/doc/html/rfc3414) — SNMPv3 USM 150-second time window
- [Cisco SNMP Counter FAQ](https://www.cisco.com/c/en/us/support/docs/ip/simple-network-management-protocol-snmp/26007-faq-snmpcounter.html) — Counter32/Counter64 wrap-around behavior
- [Quartz.NET Best Practices](https://www.quartz-scheduler.net/documentation/best-practices.html) — thread pool sizing, admin UI exposure

### Secondary (MEDIUM confidence)
- [MediatR parallel notifications — Milan Jovanovic](https://www.milanjovanovic.tech/blog/how-to-publish-mediatr-notifications-in-parallel) — TaskWhenAllPublisher pattern and DI scope caveat
- [SharpSnmpLib agent development docs](https://docs.sharpsnmp.com/samples/agent-development.html) — ObjectStore thread-safety limitation
- [OTel Prometheus Remote Write architecture](https://oneuptime.com/blog/post/2026-02-06-prometheus-remote-write-opentelemetry-collector/view) — push pipeline pattern
- [OTel graceful shutdown discussion](https://github.com/open-telemetry/opentelemetry-dotnet/discussions/3614) — ForceFlush requirement on shutdown
- [Packet Pushers: sysUpTime reboot detection](https://packetpushers.net/blog/catch-unexpected-reboots-through-monitoring-sysuptimeinstance/) — uptime-based counter reset detection
- [NuGet: MediatR 12.5.0 / 14.1.0](https://www.nuget.org/packages/MediatR) — license status, community tier eligibility

### Tertiary (LOW confidence)
- Network monitoring industry surveys (Domotz, Netflow Logic, WhatsUp Gold) — feature expectations for SNMP monitoring tools; consistent signal across sources
- [Prometheus leader election HA duplicate metrics](https://medium.com/yotpoengineering/prometheus-operator-with-leader-election-solving-duplicate-remote-write-metrics-in-ha-setup-8b6581d10b45) — single community source; pattern consistent with OTel design

---
*Research completed: 2026-03-04*
*Ready for roadmap: yes*
