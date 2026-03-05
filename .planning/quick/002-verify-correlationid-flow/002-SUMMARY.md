# Quick Task 002: Verify CorrelationId Flow

**Status:** Complete (verification only, no code changes)
**Date:** 2026-03-05

## Claim 1: CorrelationId Generation — Time-window based; generated at startup, refreshed by correlation job

**CONFIRMED**

| Point | Evidence |
|-------|----------|
| Startup seed | `Program.cs:30` — `correlationService.SetCorrelationId(Guid.NewGuid().ToString("N"))` |
| Periodic rotation | `CorrelationJob.cs:37-38` — `Guid.NewGuid().ToString("N")` + `SetCorrelationId()` on Quartz schedule |
| Default interval | `CorrelationJobOptions.cs:16` — `IntervalSeconds = 30` (configurable) |
| Single-writer model | `RotatingCorrelationService.cs:15` — `volatile string _correlationId` with startup as first writer, CorrelationJob as sole subsequent writer |

## Claim 2: CorrelationId Propagation — Trap → attach on arrival; jobs → read before execution

**PARTIALLY CONFIRMED — OperationCorrelationId is NEVER SET**

The infrastructure for per-operation correlation exists but is unused:

| Component | Propagation? | Evidence |
|-----------|-------------|----------|
| `ICorrelationService.OperationCorrelationId` | Defined | `ICorrelationService.cs:26` — AsyncLocal-backed property |
| `RotatingCorrelationService` | Implemented | `RotatingCorrelationService.cs:16` — `static AsyncLocal<string?>` |
| `SnmpTrapListenerService` | **DOES NOT SET** | No reference to ICorrelationService anywhere in file |
| `ChannelConsumerService` | **DOES NOT SET** | No reference to ICorrelationService anywhere in file |
| `MetricPollJob` | **DOES NOT SET** | No reference to ICorrelationService; no OperationCorrelationId assignment |
| `SnmpConsoleFormatter` | Reads it | `SnmpConsoleFormatter.cs:79` — `_correlationService?.OperationCorrelationId` (always null) |
| `SnmpLogEnrichmentProcessor` | Reads it | `SnmpLogEnrichmentProcessor.cs:55` — falls back to `CurrentCorrelationId` when null |

**Finding:** `OperationCorrelationId` is always null at runtime. The fallback to `CurrentCorrelationId` (global) always activates. Per-trap and per-job correlation scoping is architecturally prepared but not wired.

## Claim 3: CorrelationId Storage — in liveness vector indirectly, in all OTLP logs

**PARTIALLY CONFIRMED**

| Point | Status | Evidence |
|-------|--------|----------|
| OTLP logs | **CONFIRMED** | `SnmpLogEnrichmentProcessor.cs:54-55` — `correlationId` attribute added to every LogRecord |
| Console logs | **CONFIRMED** | `SnmpConsoleFormatter.cs:78-90` — format: `[site\|role\|globalId\|operationId]` |
| Liveness vector "indirectly" | **MISLEADING** | `CorrelationJob.cs:52` — `_liveness.Stamp(jobKey)` records a **DateTimeOffset timestamp**, not a correlationId. The liveness vector proves the CorrelationJob ran (and thus a correlationId was rotated), but stores no correlation value. |

## Summary

| Claim | Verdict |
|-------|---------|
| Generation: startup + periodic rotation | **CONFIRMED** |
| Propagation: trap → attach on arrival | **NOT IMPLEMENTED** — trap path has no ICorrelationService reference |
| Propagation: jobs → read + propagate | **NOT IMPLEMENTED** — MetricPollJob has no OperationCorrelationId assignment |
| Storage: liveness vector indirectly | **MISLEADING** — liveness stores timestamps, not correlationIds |
| Storage: all OTLP logs | **CONFIRMED** — via SnmpLogEnrichmentProcessor (falls back to global ID) |
