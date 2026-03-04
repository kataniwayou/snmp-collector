# SNMP Monitoring System

## What This Is

An SNMP monitoring system that receives traps and polls devices, resolves OIDs to human-readable metric names via a flat OID map, and pushes metrics through OpenTelemetry to Prometheus/Grafana. Built on C# .NET 9 with MediatR for event routing and Quartz for poll scheduling. Designed for multi-instance Kubernetes deployment with leader-gated metric export.

## Core Value

Every SNMP OID — from a trap or a poll — gets resolved, typed correctly (gauge/counter/info), and pushed to Prometheus where it's queryable in Grafana within seconds.

## Requirements

### Validated

(None yet — ship to validate)

### Active

**MediatR Pipeline**
- [ ] `SnmpOidReceived` notification published by both trap listener and poller
- [ ] `LoggingBehavior` — logs every OID received with agent and source
- [ ] `ExceptionBehavior` — catches all failures, never crashes the pipeline
- [ ] `ValidationBehavior` — rejects malformed OID or IP format
- [ ] `OidResolutionBehavior` — resolves `metric_name` from OID map, sets "Unknown" if not found
- [ ] `OtelMetricHandler` — records to correct instrument (gauge/counter/info) based on SNMP TypeCode

**SNMP Reception**
- [ ] Trap listener on UDP 162 (SNMPv2c, community string auth)
- [ ] Quartz-based poller with per-device configuration
- [ ] Per-device OID list with configurable poll intervals
- [ ] Both traps and polls publish the same `SnmpOidReceived` event to MediatR

**Business Metrics — Three Instruments**
- [ ] `snmp_gauge` — for Integer32, Gauge32, TimeTicks (point-in-time value)
- [ ] `snmp_counter` — for Counter32, Counter64 (delta computation with cache, supports `rate()`)
- [ ] `snmp_info` — for OctetString, IpAddress, OID (gauge=1, string in `value` label)
- [ ] Common labels on all: `site_name`, `metric_name`, `oid`, `agent`, `source`
- [ ] `site_name` loaded from appsettings `Site:Name`
- [ ] `source` = "poll" or "trap"

**Counter Delta Handling**
- [ ] Cache stores last cumulative value per OID+agent
- [ ] Delta = current - previous (normal case)
- [ ] Counter reset: current < previous → treat current as delta
- [ ] First poll after startup: skip (no previous baseline)

**OID Map**
- [ ] Flat `Dictionary<string, string>` in appsettings under `OidMap` section
- [ ] Maps OID string → metric_name (e.g., `"1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad"`)
- [ ] OID in map → resolved metric_name
- [ ] OID not in map → `metric_name = "Unknown"`
- [ ] Shared by traps and polls — no device distinction
- [ ] Covers both NPB (enterprise 47477.100) and OBP (enterprise 47477.10.21) devices
- [ ] Index baked into metric name for per-port/per-link OIDs (e.g., `obp_r1_power_l1`, `obp_r1_power_l2`)

**Pipeline Metrics (System Health)**
- [ ] `snmp.event.published` — events published to MediatR
- [ ] `snmp.event.handled` — events successfully handled by OtelMetricHandler
- [ ] `snmp.event.errors` — exceptions caught by ExceptionBehavior
- [ ] `snmp.event.rejected` — events rejected by ValidationBehavior
- [ ] `snmp.poll.executed` — Quartz poll jobs completed
- [ ] `snmp.trap.received` — traps received on UDP 162
- [ ] All pipeline metrics include `site_name` label

**Logging & Telemetry (Simetra Patterns)**
- [ ] Structured logging with `ILogger` and named placeholders
- [ ] Custom console formatter: `[timestamp] [level] [site|role|correlationId] category message`
- [ ] OTLP log export with enrichment processor (site_name, role, correlationId)
- [ ] Correlation IDs: rotating global (CorrelationJob) + per-operation AsyncLocal scoping
- [ ] Console output toggleable via `Logging:EnableConsole` in appsettings
- [ ] No traces — metrics and logs only

**Leader Election / HA**
- [ ] `ILeaderElection` interface (`IsLeader`, `CurrentRole`)
- [ ] `K8sLeaseElection` — Kubernetes Lease API for production (auto-detected)
- [ ] `AlwaysLeaderElection` — always leader for local dev
- [ ] All instances poll devices and receive traps (not leader-only)
- [ ] Only leader exports business metrics (snmp_gauge, snmp_counter, snmp_info)
- [ ] Pipeline metrics + System.Runtime exported by all instances
- [ ] `MetricRoleGatedExporter` filters business meter at export time
- [ ] Near-instant failover via explicit lease deletion on shutdown

**Quartz Scheduling**
- [ ] Quartz fires `MetricPollJob` per device/poll combination
- [ ] Intervals configurable per OID group per device
- [ ] Thread pool auto-scaled to job count
- [ ] CorrelationJob rotates correlation ID on schedule

**Push Pipeline**
- [ ] App → OTLP gRPC → OTel Collector → `remote_write` → Prometheus → Grafana
- [ ] Full push — nothing pulls

### Out of Scope

- Device management / configuration writes — monitor only, no SNMP SET
- Custom middleware pipeline — using MediatR, not Simetra's custom pipeline
- Device modules (`IDeviceModule`) — device-agnostic, flat OID map only
- Channel queuing (`System.Threading.Channels`) — MediatR direct dispatch
- State vector / in-memory device state tracking
- Traces / distributed tracing — no TracerProvider, no ActivitySource
- ObservableCounter / ObservableGauge — using regular Counter.Add(delta) and Gauge.Record(value)
- OID prefix/pattern matching — flat exact-match dictionary only
- Per-OID metric names — using three shared instruments (snmp_gauge, snmp_counter, snmp_info)
- `raw_value` label on gauge/counter metrics — only `snmp_info` carries string `value` label

## Context

**Reference project:** `src/Simetra/` is an existing SNMP monitoring system used as architectural reference. Key patterns adopted: structured logging, OTel setup, console formatter, correlation IDs, leader election, role-gated export. Key patterns replaced: custom middleware → MediatR, device modules → flat OID map, channels → direct dispatch.

**Target devices:** NPB (Network Packet Broker, CGS enterprise 47477.100) and OBP (Optical Bypass, CGS enterprise 47477.10.21). Both share the same OID map — the system doesn't distinguish device types.

**NPB MIB coverage:** System health (CPU, memory, uptime), port statistics (RX/TX octets, utilization), filter memory, HA status, transceiver health, inline tool status, load balancing, traps (link up/down, temperature, PSU, utilization thresholds).

**OBP coverage:** 8 optical bypass links with per-link state, channel, optical power (R1-R4), heartbeat status, power alarm status, and per-link traps.

**OID map sizing:** ~500+ entries covering both device families. Per-port/per-link OIDs are expanded (e.g., 8 links x 4 power readings = 32 OBP power entries). Index baked into metric name (e.g., `obp_r1_power_l3`).

**Grafana dashboard strategy:** Separate table panels per metric type — gauge table for device status, info table for metadata, counter-based panels with `rate()` for bandwidth. Combined view available via `{__name__=~"snmp_(gauge|counter_total|info)"}`.

## Constraints

- **Runtime**: C# .NET 9
- **Event routing**: MediatR — pipeline behaviors for cross-cutting concerns
- **Scheduling**: Quartz.NET — in-memory store, dynamic job registration
- **SNMP library**: SharpSnmpLib (Lextm) — SNMPv2c
- **Telemetry**: OpenTelemetry SDK with OTLP gRPC exporter — metrics and logs only
- **HA**: Kubernetes Lease API for leader election — all instances active, export gated
- **Metric design**: Three instruments (snmp_gauge, snmp_counter, snmp_info) — type determined at runtime from SNMP TypeCode
- **OID map**: Flat dictionary, no pattern matching — exact OID string lookup

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| MediatR over custom middleware | Simpler extension model, familiar CQRS pattern, testable with in-memory bus | — Pending |
| Three instruments over one gauge | Correct Prometheus types, `rate()` works on counters, strings in info metric | — Pending |
| Flat OID map over device modules | Device-agnostic, config-only OID changes, no code deployment for new OIDs | — Pending |
| Delta computation for counters | Regular Counter.Add(delta) avoids Observable pattern complexity | — Pending |
| No raw_value label on gauge/counter | Prevents cardinality explosion from changing numeric values as labels | — Pending |
| All instances poll and receive | No warm-up delay on failover, leader only controls metric export | — Pending |
| No traces | Metrics and logs sufficient for SNMP monitoring, reduces complexity | — Pending |
| metric_name over human_name | Clearer semantics for the label that carries the resolved OID name | — Pending |
| Index in metric name (e.g., _l1) | Flat OID map stays simple, avoids prefix matching complexity | — Pending |
| Unknown for unmapped OIDs | Discovery mechanism — visible in Grafana, can identify OIDs to add to map | — Pending |

---
*Last updated: 2026-03-04 after initialization*
