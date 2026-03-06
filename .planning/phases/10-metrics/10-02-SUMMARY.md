---
phase: "10-metrics"
plan: "02"
subsystem: "telemetry"
tags: ["otel", "metrics", "labels", "host_name", "device_name"]
dependency_graph:
  requires: ["10-01"]
  provides: ["metric-label-taxonomy", "hostname-resolution-pattern"]
  affects: ["10-04", "10-05"]
tech_stack:
  added: []
  patterns: ["HOSTNAME env var fallback to Environment.MachineName for host identity"]
key_files:
  created: []
  modified:
    - "src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs"
    - "src/SnmpCollector/Telemetry/SnmpMetricFactory.cs"
    - "src/SnmpCollector/Telemetry/PipelineMetricService.cs"
    - "src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs"
    - "src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs"
    - "src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs"
    - "src/SnmpCollector/Pipeline/CardinalityAuditService.cs"
decisions:
  - id: "10-02-01"
    description: "host_name resolved from HOSTNAME env var with Environment.MachineName fallback — consistent across SnmpMetricFactory, PipelineMetricService, and SnmpConsoleFormatter"
  - id: "10-02-02"
    description: "agent label split into device_name + ip as two separate metric labels — device_name from community string, ip from sender/target address"
  - id: "10-02-03"
    description: "SiteOptions dependency removed from SnmpMetricFactory, PipelineMetricService, and SnmpConsoleFormatter — host identity is environment-derived, not configuration-derived"
metrics:
  duration: "~6 min"
  completed: "2026-03-06"
---

# Phase 10 Plan 02: Metric Label Taxonomy Change Summary

**One-liner:** Replace site_name with host_name (from HOSTNAME env), split agent into device_name + ip across all metric instruments, pipeline counters, log enrichment, and console formatter.

## Tasks Completed

| # | Task | Commit | Key Changes |
|---|------|--------|-------------|
| 1 | ISnmpMetricFactory, SnmpMetricFactory, PipelineMetricService label changes | 05f1dec | Interface params: agent -> deviceName + ip; host_name label; remove SiteOptions |
| 2 | OtelMetricHandler, log enrichment, console formatter, cardinality audit | 41f33d3 | Handler passes deviceName + ip; console shows hostname; log enrichment uses host_name |

## Decisions Made

1. **HOSTNAME env var pattern** — All three services (SnmpMetricFactory, PipelineMetricService, SnmpConsoleFormatter) resolve hostname identically: `Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName`. In K8s, HOSTNAME is the pod name. In local dev, MachineName is used.

2. **agent -> device_name + ip split** — The single `agent` parameter (which was `DeviceName ?? AgentIp.ToString()`) is now two explicit parameters: `deviceName` (from community string convention, defaulting to "unknown") and `ip` (always `AgentIp.ToString()`). This provides better Prometheus query granularity.

3. **SiteOptions removed from telemetry** — SnmpMetricFactory, PipelineMetricService, and SnmpConsoleFormatter no longer depend on `IOptions<SiteOptions>`. Host identity comes from the environment, not configuration. SnmpLogEnrichmentProcessor parameter renamed from `siteName` to `hostName`.

## Verification Results

- `grep site_name` across all 7 modified files: zero matches
- `grep host_name` in SnmpMetricFactory: 2 matches (RecordGauge + RecordInfo)
- `grep host_name` in PipelineMetricService: 11 matches (all Increment* methods)
- OtelMetricHandler passes `deviceName, ip` (2 args) to all 8 RecordGauge/RecordInfo calls
- SnmpConsoleFormatter has zero SiteOptions references
- Build: no errors in modified files (6 pre-existing IDeviceChannelManager errors from other plans)

## Deviations from Plan

None — plan executed exactly as written.

## Next Phase Readiness

- Plan 03 (trap path refactoring) can proceed — ISnmpMetricFactory interface now has correct signatures
- Plan 04 (DI wiring) must update: SnmpMetricFactory and PipelineMetricService constructors no longer take IOptions<SiteOptions>
- Plan 04 must update: SnmpLogEnrichmentProcessor DI wiring passes hostName string (not siteName)
