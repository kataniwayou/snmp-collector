# Stack Research

**Domain:** SNMP Monitoring System (C# .NET 9)
**Researched:** 2026-03-04
**Confidence:** HIGH (all primary choices verified against NuGet/official sources)

---

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET 9 | 9.x (LTS via .NET 8, current) | Runtime | Project constraint. All libraries below target net8.0+ and run on net9.0 without issues. |
| Lextm.SharpSnmpLib | 12.5.7 (stable) | SNMP protocol — trap reception and device polling | Only actively-maintained, MIT-licensed SNMP library for modern .NET. Supports GET, GETBULK, WALK, SNMPv2c trap reception, and INFORM. SnmpSharpNet is last-published in 2017 and abandoned. |
| MediatR | 14.1.0 (commercial/free tier) OR 12.5.0 (MIT) | In-process event routing and pipeline behaviors | De-facto standard for CQRS/mediator pattern in .NET. v14 supports net9.0 natively. **Licensing decision required** — see note below. |
| Quartz.NET | 3.16.0 | Poll scheduling (cron/interval-based job triggers) | Supports .NET 9 explicitly. Superior to Hangfire for infrastructure daemons: misfire handling, job-key-based concurrency prevention, no persistent storage required (RAM scheduler), clustering via Kubernetes leader election instead. Hangfire requires a database just to run. |
| OpenTelemetry SDK | 1.15.0 | Metrics and log pipelines | Official, stable. Targets net9.0. Provides `ObservableGauge` (snmp_gauge), `ObservableCounter` (snmp_counter), and attribute-bearing instruments (snmp_info pattern). Ships with ILogger bridge out of the box. |
| OpenTelemetry OTLP Exporter | 1.15.0 | Push metrics/logs via gRPC to OTel Collector | Default protocol is gRPC/protobuf. Handles retry, batching, and mTLS (added in 1.15.0). Required for the App → OTLP → Collector pipeline. |
| OpenTelemetry Extensions Hosting | 1.15.0 | IHostedService integration for MeterProvider/LoggerProvider lifecycle | Wires OTel SDK into .NET Generic Host startup/shutdown cleanly. Required; otherwise providers must be managed manually. |
| KubernetesClient | 19.0.2 | Kubernetes Lease API for leader election | Official C# client from kubernetes-client/csharp. Includes `LeaderElector` with `LeaseLock` out of the box. Targets net8.0/net9.0/net10.0. Apache-2.0 license. |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.Hosting | (via .NET 9 SDK) | Generic Host, DI, IHostedService | Always — forms the application backbone |
| Microsoft.Extensions.Logging | (via .NET 9 SDK) | ILogger abstraction | Always — OTel bridges ILogger to OTLP logs automatically |
| Microsoft.Extensions.Options | (via .NET 9 SDK) | Strongly-typed configuration (IOptions<T>) | Per polling target configuration, OID map config |
| System.Diagnostics.Metrics | (via .NET 9 BCL) | `Meter`, `ObservableGauge<T>`, `ObservableCounter<T>` | Core instrumentation API; prefer BCL types over OTel abstractions for instrument creation |
| Lextm.SharpSnmpLib.Engine | 12.5.7 | SNMP agent/listener engine for trap reception | Required when hosting a `SnmpEngine` / `Listener` that receives inbound V2 traps on UDP 162 |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| dotnet-counters | Local metric verification without a full OTel stack | `dotnet tool install -g dotnet-counters` — verifies meters emit correctly before wiring OTLP |
| Prometheus + Grafana (Docker Compose) | Local end-to-end pipeline validation | Run OTel Collector → remote_write → Prometheus locally to verify the full push path |
| OTel Collector (contrib) | Local collector for dev; production collector in Kubernetes | Use `otelcol-contrib` for access to `prometheusremotewriteexporter` |

---

## MediatR Licensing Note

MediatR changed from MIT to a dual-license model (RPL-1.5 + commercial) starting in v13.0, released July 2025.

**Options:**

1. **MediatR 14.1.0 — Community tier (free):** Organizations under $5M gross annual revenue and under $10M in outside capital are eligible for the free Community commercial license. Most internal/infrastructure tooling fits this. Verify eligibility before shipping.

2. **MediatR 12.5.0 — MIT (last free OSS version):** Fully MIT-licensed, no strings attached. Targets net8.0 and runs on net9.0 via compatibility. Lacks any features added in v13/v14 (pipeline behavior changes are minimal). This is a safe choice if licensing uncertainty is unacceptable.

3. **Replace with custom pipeline:** For this domain (SNMP trap routing, poll dispatch), MediatR is used for pipeline behaviors (logging, error handling, telemetry decorators) and internal event routing. A custom `IMessageBus`/`IEventDispatcher` with a single pipeline chain is ~200 lines and eliminates the dependency entirely.

**Recommendation:** Use 12.5.0 (MIT) unless the team is already licensed for MediatR commercial use. The pipeline behaviors pattern it enables is valuable, but the library is replaceable. Do not use a library with uncertain license terms in an infrastructure component that may be redistributed.

---

## Installation

```bash
# Core SNMP
dotnet add package Lextm.SharpSnmpLib --version 12.5.7
dotnet add package Lextm.SharpSnmpLib.Engine --version 12.5.7

# Scheduling
dotnet add package Quartz --version 3.16.0
dotnet add package Quartz.Extensions.Hosting --version 3.16.0
dotnet add package Quartz.Extensions.DependencyInjection --version 3.16.0

# MediatR (choose one)
dotnet add package MediatR --version 12.5.0   # MIT, last free version
# OR
dotnet add package MediatR --version 14.1.0   # Commercial (free Community tier eligible)

# OpenTelemetry
dotnet add package OpenTelemetry --version 1.15.0
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.15.0
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.15.0

# Kubernetes leader election
dotnet add package KubernetesClient --version 19.0.2
```

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| Lextm.SharpSnmpLib | SnmpSharpNet | Never — last published 2017, abandoned, no .NET Core support |
| Lextm.SharpSnmpLib | OTel Collector SNMP Receiver | If you want no custom SNMP code: deploy the OTel Collector with `snmpreceiver` instead of writing C# polling. Appropriate when the app is config-only, but loses custom OID-to-metric mapping flexibility. |
| Quartz.NET | Hangfire | Use Hangfire only if you need a persistent job queue with retry history dashboard. For SNMP polling, Hangfire's database dependency is unnecessary overhead. |
| Quartz.NET | `PeriodicTimer` (BCL) | Use for a single fixed-interval poll loop with no cron needs. Acceptable for extremely simple single-target scenarios; does not support per-target schedule configuration. |
| Quartz.NET | NCrontab + `IHostedService` | Viable for cron parsing without full Quartz, but reimplements misfire handling and concurrency control manually. |
| KubernetesClient (LeaderElector) | Steeltoe.Discovery | Steeltoe targets service discovery, not leader election. Not applicable. |
| KubernetesClient (LeaderElector) | Custom Redis lock | Adds Redis dependency for a problem Kubernetes Lease API solves natively in-cluster. |
| OpenTelemetry SDK | Prometheus .NET client (prometheus-net) | Use if you want to expose a scrape endpoint instead of pushing. Incompatible with the chosen push pipeline (App → OTLP → Collector → remote_write). Do not mix both approaches. |
| OTLP gRPC | OTLP HTTP/protobuf | Use HTTP if gRPC is blocked by corporate proxies or firewall. Functionally equivalent; gRPC is the OTel default and preferred for internal Kubernetes traffic. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| SnmpSharpNet | Abandoned since 2017. No .NET Standard or .NET Core support. Will not compile against modern TFMs without forking. | Lextm.SharpSnmpLib 12.5.7 |
| Grpc.Core (legacy) | Deprecated package previously used by OTel OTLP exporter. OTel 1.15.0 removed this dependency. Do not add it. | `Grpc.Net.Client` (included transitively via OTLP exporter) |
| Hangfire (with SQL Server/Redis) | Database-backed job storage adds operational complexity. SNMP polling does not need durable job queues — a schedule is deterministic from config. | Quartz.NET with RAM scheduler |
| prometheus-net scrape endpoint | Creates an HTTP /metrics endpoint for Prometheus to poll. Contradicts the push pipeline design (App → OTLP → Collector → remote_write). Dual-mode operation creates confusion and duplicate metrics. | OpenTelemetry OTLP exporter |
| MediatR v13+ without license | RPL-1.5 is a copyleft license requiring source disclosure for derived works. For internal infrastructure tools the risk is low, but the ambiguity is real. Do not accept the risk without legal review. | MediatR 12.5.0 (MIT) or custom dispatcher |
| Manual OID string dictionaries at call sites | Hardcoding OID strings throughout the polling code makes MIB changes a scattered refactor. | Central flat-map `OidRegistry` loaded from config at startup |

---

## Stack Patterns by Variant

**If running as a standalone daemon (no Kubernetes):**
- Remove KubernetesClient entirely
- Use a single-instance deployment; no leader election needed
- Quartz.NET RAM scheduler still applies

**If SNMPv3 support is required later:**
- SharpSnmpLib 12.5.7 supports SNMPv3 (auth/privacy) natively
- No library change; add `Discovery` and `SecureString` configuration
- The `Messenger` and `Listener` APIs are version-agnostic at call sites

**If the OID flat-map grows large (>10,000 entries):**
- Load from a YAML/JSON file at startup, not compiled constants
- Consider a compiled lookup (frozen dictionary) for sub-microsecond reads

**If traces are added later:**
- Add `OpenTelemetry.Extensions.Hosting` trace configuration
- No new packages needed; OTLP exporter handles all three signals
- `System.Diagnostics.Activity` is the BCL type for spans

---

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| Lextm.SharpSnmpLib 12.5.7 | .NET 8.0+ | Targets net8.0; runs on .NET 9 via forward compatibility |
| MediatR 12.5.0 | .NET 8.0+ | MIT license, targets net8.0; runs on .NET 9 |
| MediatR 14.1.0 | .NET 8.0, 9.0, 10.0 | Explicit net9.0 target; commercial license |
| Quartz 3.16.0 | .NET 8.0, 9.0, 10.0 | Explicit net9.0 target |
| OpenTelemetry 1.15.0 | .NET 8.0, 9.0, 10.0 | Explicit net9.0 target |
| KubernetesClient 19.0.2 | .NET 8.0, 9.0, 10.0 | Explicit net9.0 target |
| All OTel packages | Must be same version | OTel SDK, OTLP Exporter, and Extensions.Hosting must all be 1.15.0 — mixed versions cause runtime errors |

---

## Key Design Decisions Validated

### snmp_info as a Metric Instrument

The project uses `snmp_info` as an "info" metric (device metadata: hostname, firmware version, location). OpenTelemetry does not have a dedicated `Info` instrument type in the .NET SDK — the standard pattern is an `ObservableGauge<long>` (always value=1) with the informational dimensions as attributes. This matches the Prometheus convention (`target_info{...} 1`).

```csharp
meter.CreateObservableGauge<long>(
    "snmp_info",
    () => new Measurement<long>(1, new TagList
    {
        { "device", hostname },
        { "firmware", firmwareVersion },
        { "location", location }
    }),
    description: "Device identity attributes"
);
```

Confidence: HIGH — verified against OTel .NET metrics best-practices documentation and OTel data model spec.

### snmp_gauge as ObservableGauge

SNMP gauge OIDs (e.g., CPU load, temperature, interface utilization) map to `ObservableGauge<double>`. The callback reads the last polled value from a thread-safe cache updated by Quartz jobs. This avoids blocking the OTel collection thread.

### snmp_counter as ObservableCounter

SNMP counter OIDs (e.g., ifInOctets, ifOutOctets) map to `ObservableCounter<long>`. These are monotonically increasing values; OTel SDK handles the rate calculation at the Prometheus/Grafana layer.

### Push Pipeline Confirmed

The App → OTLP gRPC → OTel Collector → `prometheusremotewriteexporter` → Prometheus → Grafana pipeline is well-established and documented. The OTel Collector contrib distribution includes `prometheusremotewriteexporter`. The OTLP exporter in the app only requires an endpoint URL and optional auth header — no Prometheus scrape endpoint is needed in the application.

---

## Sources

- [NuGet: Lextm.SharpSnmpLib 12.5.7](https://www.nuget.org/packages/Lextm.SharpSnmpLib) — version and platform targets verified
- [GitHub: lextudio/sharpsnmplib releases](https://github.com/lextudio/sharpsnmplib/releases) — 12.5.7 latest stable (Feb 27, 2025)
- [SharpSnmpLib docs: Introduction](https://docs.lextudio.com/sharpsnmplib/tutorials/introduction) — GET, GETBULK, trap operations confirmed
- [NuGet: MediatR 14.1.0](https://www.nuget.org/packages/MediatR) — version, .NET targets, commercial license (Mar 3, 2026)
- [NuGet: MediatR 12.5.0](https://www.nuget.org/packages/MediatR/12.5.0) — last MIT-licensed version confirmed
- [mediatr.io pricing](https://mediatr.io/) — Community tier: free under $5M revenue, verified Mar 2026
- [NuGet: Quartz 3.16.0](https://www.nuget.org/packages/Quartz) — version, .NET 9 target confirmed (Mar 1, 2026)
- [NuGet: OpenTelemetry 1.15.0](https://www.nuget.org/packages/OpenTelemetry) — stable, net9.0 target (Jan 21, 2026)
- [NuGet: OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol) — OTLP gRPC exporter, stable
- [NuGet: OpenTelemetry.Extensions.Hosting 1.15.0](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting) — hosting integration
- [NuGet: KubernetesClient 19.0.2](https://www.nuget.org/packages/KubernetesClient) — net9.0 target, Apache-2.0 (Feb 24, 2026)
- [OTel .NET Metrics Instruments](https://opentelemetry.io/docs/languages/dotnet/metrics/instruments/) — instrument types and ObservableGauge confirmed
- [OTel .NET Metrics Best Practices](https://opentelemetry.io/docs/languages/dotnet/metrics/best-practices/) — info metric pattern, cardinality limits
- [OTel Prometheus Remote Write architecture](https://oneuptime.com/blog/post/2026-02-06-prometheus-remote-write-opentelemetry-collector/view) — push pipeline pattern (MEDIUM confidence, cross-referenced with OTel docs)

---
*Stack research for: SNMP Monitoring System (C# .NET 9)*
*Researched: 2026-03-04*
