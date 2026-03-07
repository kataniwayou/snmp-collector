# SNMP Monitoring System

## What This Is

A K8s-native SNMP monitoring agent that receives traps and polls devices, resolves OIDs to human-readable metric names via a flat OID map, and pushes metrics through OpenTelemetry to Prometheus/Grafana. Built on C# .NET 9 with MediatR for event routing and Quartz for poll scheduling. Runs as a 3-replica Kubernetes deployment with leader-gated metric export and near-instant failover.

## Core Value

Every SNMP OID — from a trap or a poll — gets resolved, typed correctly (gauge/info), and pushed to Prometheus where it's queryable in Grafana within seconds.

## Requirements

### Validated

**v1.0 Foundation (shipped 2026-03-07)**

- MediatR pipeline: 4-behavior chain (Logging -> Exception -> Validation -> OidResolution) with OtelMetricHandler
- SNMP trap listener with community string convention (Simetra.*) and backpressure channel
- Quartz-based SNMP GET polling with per-device configuration and unreachability tracking
- Two metric instruments: snmp_gauge (Integer32, Gauge32, TimeTicks, Counter32, Counter64) and snmp_info (OctetString, IpAddress, OID)
- Flat OID map with hot-reload, "Unknown" fallback for unmapped OIDs
- Pipeline metrics (6 counters) exported by all instances
- K8s Lease API leader election with MetricRoleGatedExporter
- Graceful 5-step shutdown (30s budget) with lease release
- Startup/readiness/liveness health probes with per-job staleness detection
- HeartbeatJob loopback proving pipeline liveness (suppressed from metric export)
- Label taxonomy: host_name, pod_name, metric_name, oid, device_name, ip, source, snmp_type

See `.planning/milestones/v1.0-REQUIREMENTS.md` for full requirement details.

### Active

**v1.1 Device Simulation (in progress)**

- OID map structure and naming conventions for NPB + OBP device families
- Poll OID documentation (values, ranges, units, expected behavior)
- Simulator refinement — selective trap/poll behavior matching real device profiles
- K8s simulator pod deployment integrated with snmp-collector

### Out of Scope

- Device management / configuration writes — monitor only, no SNMP SET
- Custom middleware pipeline — using MediatR
- Device modules (`IDeviceModule`) — device-agnostic, flat OID map only
- Traces / distributed tracing — no TracerProvider, no ActivitySource
- OID prefix/pattern matching — flat exact-match dictionary only
- Per-OID metric names — using two shared instruments (snmp_gauge, snmp_info)
- `raw_value` label on gauge metrics — only snmp_info carries string `value` label
- SNMPv3 auth / USM security — target devices use v2c

## Context

**Current state:** v1.0 shipped. 4,077 LOC source + 3,742 LOC tests across 94 C# files. 121 tests passing. Running in Docker Desktop K8s cluster (3 replicas) with OTel Collector + Prometheus + Grafana.

**Reference project:** `src/Simetra/` is an existing SNMP monitoring system used as architectural reference. Key patterns adopted: structured logging, OTel setup, console formatter, correlation IDs, leader election, role-gated export. Key patterns replaced: custom middleware -> MediatR, device modules -> flat OID map, channels -> single shared trap channel.

**Target devices:** NPB (Network Packet Broker, CGS enterprise 47477.100) and OBP (Optical Bypass, CGS enterprise 47477.10.21). Both share the same OID map.

**OID map sizing:** ~500+ entries covering both device families. Per-port/per-link OIDs expanded with index baked into metric name (e.g., `obp_r1_power_l3`).

**Known tech debt:**
- `IDeviceRegistry.TryGetDevice(IPAddress)` orphaned (community string replaced IP lookup)
- `PollSchedulerStartupService` thread pool log off-by-one (HeartbeatJob not counted)

## Constraints

- **Runtime**: C# .NET 9
- **Event routing**: MediatR 12.5.0 (MIT) — v13+ is RPL-1.5, do not upgrade
- **Scheduling**: Quartz.NET — in-memory store, dynamic job registration
- **SNMP library**: SharpSnmpLib (Lextm) — SNMPv2c only
- **Telemetry**: OpenTelemetry SDK with OTLP gRPC exporter — metrics and logs only
- **HA**: Kubernetes Lease API for leader election — all instances active, export gated
- **Metric design**: Two instruments (snmp_gauge, snmp_info) — type determined at runtime from SNMP TypeCode
- **OID map**: Flat dictionary, no pattern matching — exact OID string lookup
- **Community string**: Simetra.{DeviceName} convention for both auth and device identity

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| MediatR over custom middleware | Simpler extension model, familiar CQRS pattern, testable with in-memory bus | Good |
| Two instruments (gauge + info) | Correct Prometheus types, counters as raw gauges let Prometheus rate() handle delta | Good |
| Flat OID map over device modules | Device-agnostic, config-only OID changes, no code deployment for new OIDs | Good |
| Counter delta removed | Prometheus rate() handles natively; in-app delta was unnecessary complexity | Good |
| No raw_value label on gauge | Prevents cardinality explosion from changing numeric values as labels | Good |
| All instances poll and receive | No warm-up delay on failover, leader only controls metric export | Good |
| No traces | Metrics and logs sufficient for SNMP monitoring, reduces complexity | Good |
| Community string convention | Simetra.{DeviceName} replaces IP-based device lookup; works for any IP | Good |
| host_name from NODE_NAME | Physical K8s node identity (persistent), pod_name from HOSTNAME (ephemeral) | Good |
| Heartbeat as internal infra | Pipeline metrics prove liveness; no snmp_gauge pollution for heartbeat | Good |
| IRequest<Unit> over INotification | MediatR behaviors only fire for IRequest; INotification bypasses pipeline entirely | Good |

---
*Last updated: 2026-03-07 after v1.1 milestone start*
