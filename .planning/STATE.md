# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-04)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** Phase 1 — Infrastructure Foundation

## Current Position

Phase: 1 of 8 (Infrastructure Foundation)
Plan: 3 of 5 in current phase
Status: In progress
Last activity: 2026-03-05 — Completed 01-03-PLAN.md (correlation service, formatter, log enrichment processor)

Progress: [░░░░░░░░░░] 7% (3/40 plans across all phases estimated)

## Performance Metrics

**Velocity:**
- Total plans completed: 3
- Average duration: ~3 min
- Total execution time: ~9 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-infrastructure-foundation | 3 | ~9 min | ~3 min |

**Recent Trend:**
- Last 5 plans: 01-01 (scaffold), 01-02 (docker configs), 01-03 (telemetry classes)
- Trend: Fast execution, source porting tasks slightly longer than config-only

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Init]: MediatR 12.5.0 (MIT) locked — do not upgrade to v13+ (RPL-1.5 license)
- [Init]: SNMPv2c only — no v3 auth/USM
- [Init]: All instances poll and receive traps — leader election gates metric export only
- [Init]: Counter delta engine is its own phase (Phase 4) — correctness risk, no shortcuts
- [Init]: OTel cardinality must be locked in Phase 2 before any instruments are created in Phase 3
- [01-01]: Microsoft.NET.Sdk (Generic Host) not Microsoft.NET.Sdk.Web — SnmpCollector has no HTTP surface
- [01-01]: Quartz.Extensions.Hosting (not Quartz.AspNetCore) — correct package for Generic Host
- [01-01]: SiteOptions.Role = "standalone" default replaces ILeaderElection for Phase 1; Phase 7 makes it dynamic
- [01-01]: Microsoft.Extensions.Hosting 9.0.0 added explicitly — non-Web SDK requires it for Host.CreateApplicationBuilder
- [01-01]: OtlpOptions.ServiceName defaults to "snmp-collector" (not "simetra-supervisor")
- [01-02]: prometheusremotewrite exporter used (not prometheus scrape exporter) — PUSH-03 compliance
- [01-02]: otel/opentelemetry-collector-contrib image required (not core) — prometheusremotewrite is contrib-only
- [01-02]: Prometheus --web.enable-remote-write-receiver flag mandatory — without it all remote_write pushes rejected with HTTP 405
- [01-02]: resource_to_telemetry_conversion.enabled: true propagates OTel resource attributes as Prometheus labels
- [01-03]: SnmpConsoleFormatter shows BOTH globalId and operationId (Simetra showed operationId OR globalId)
- [01-03]: SnmpLogEnrichmentProcessor takes string role not Func<string> — static Phase 1, dynamic Phase 7
- [01-03]: TelemetryConstants has single MeterName (no LeaderMeterName/InstanceMeterName split — no role-gating in Phase 1)
- [01-03]: AsyncLocal<string?> _operationCorrelationId MUST be static — instance AsyncLocal does not flow through async context

### Pending Todos

None yet.

### Blockers/Concerns

- [Phase 6] MetricRoleGatedExporter uses reflection to set internal ParentProvider on BaseExporter<Metric> — verify against OTel 1.15.0 internals and add breakage-detection test during Phase 7 planning
- [Phase 4] Counter delta wrap-around and sysUpTime reboot detection require explicit unit test cases before any counter metrics reach Prometheus — design before coding

## Session Continuity

Last session: 2026-03-05
Stopped at: Completed 01-03-PLAN.md — correlation service and telemetry classes complete, 3 of 5 phase plans done, next is 01-04 (DI extension methods)
Resume file: None
