---
phase: 03-mediatr-pipeline-and-instruments
plan: 03
subsystem: pipeline
tags: [mediatr, pipeline-behavior, validation, oid-resolution, snmp, metrics, csharp]

# Dependency graph
requires:
  - phase: 03-01
    provides: SnmpOidReceived notification, PipelineMetricService counters, IPipelineBehavior pattern
  - phase: 02-02
    provides: IDeviceRegistry (TryGetDevice by IP), IOidMapService (Resolve OID to metric name)

provides:
  - ValidationBehavior: rejects malformed OIDs and unknown-device notifications with Warning log + rejected counter
  - OidResolutionBehavior: resolves OID to MetricName via IOidMapService in-place before passing downstream

affects:
  - 03-04 (PublishBehavior / handler registration — pipeline order matters)
  - 03-05 (counter increment tests depend on PipelineMetricService.IncrementRejected behavior)
  - 03-06 (DI registration — behaviors must be registered in correct pipeline order)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Open-generic IPipelineBehavior<TNotification, TResponse> where TNotification : INotification — type-safe pass-through for non-SnmpOidReceived notifications"
    - "Static compiled Regex (RegexOptions.Compiled) for OID format validation — avoids per-call JIT overhead"
    - "In-place property enrichment on sealed notification class — behaviors mutate DeviceName and MetricName as pipeline progresses"
    - "Short-circuit via return default! — MediatR notification pipeline has TResponse=Unit; default! is safe sentinel for rejection"

key-files:
  created:
    - src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs
    - src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
  modified: []

key-decisions:
  - "ValidationBehavior checks DeviceName is null before calling TryGetDevice — poll path sets DeviceName at publish time, only trap path needs registry resolution"
  - "OidResolutionBehavior always calls next() — MetricName=Unknown is a valid passthrough; handlers decide what to do with unresolved OIDs"
  - "return default! for rejection, not throw — short-circuit without exception keeps pipeline overhead low and avoids error counter double-counting"

patterns-established:
  - "Behavior pattern: if (notification is not SnmpOidReceived) return await next() — all behaviors use this guard as first line"
  - "Rejection pattern: Log Warning → IncrementRejected → return default! — consistent across all validation failures"
  - "Enrichment pattern: mutate msg.Property then always call next() — OidResolutionBehavior sets the template"

# Metrics
duration: 1min
completed: 2026-03-05
---

# Phase 3 Plan 03: Validation and OID Resolution Behaviors Summary

**Two MediatR pipeline behaviors: ValidationBehavior rejects malformed OIDs and unknown devices with Warning logs and rejected counter; OidResolutionBehavior resolves every OID to a MetricName via IOidMapService before passing downstream.**

## Performance

- **Duration:** ~1 min
- **Started:** 2026-03-05T01:37:34Z
- **Completed:** 2026-03-05T01:38:33Z
- **Tasks:** 2
- **Files modified:** 2 (both created)

## Accomplishments

- ValidationBehavior rejects notifications with OIDs not matching `^\d+(\.\d+){1,}$` (at least 2 arcs), logs Warning with Oid/AgentIp/Reason, increments PipelineMetricService.IncrementRejected(), and short-circuits via `return default!`
- ValidationBehavior also rejects notifications from unknown devices when DeviceName is null and IDeviceRegistry.TryGetDevice returns false; enriches DeviceName when device is found
- OidResolutionBehavior sets `msg.MetricName = _oidMapService.Resolve(msg.Oid)` in-place then always calls next() — never short-circuits
- Both behaviors are open-generic over TNotification : INotification; non-SnmpOidReceived types pass straight to next() via early guard

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ValidationBehavior** - `7874ef0` (feat)
2. **Task 2: Create OidResolutionBehavior** - `80f19b9` (feat)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` - Pipeline behavior that validates OID format (compiled regex) and device registry membership; rejects with Warning+counter+short-circuit
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` - Pipeline behavior that resolves msg.MetricName from IOidMapService.Resolve; always passes to next()

## Decisions Made

- ValidationBehavior checks `msg.DeviceName is null` before calling `TryGetDevice` — the poll path already sets DeviceName at publish time, so only the trap path needs registry lookup. This avoids an unnecessary registry call on every poll notification.
- OidResolutionBehavior always calls next() even when MetricName resolves to the Unknown sentinel — the downstream handler (Phase 3 plans 04-06) decides whether to discard or emit an Unknown-named metric. No silent data loss.
- Rejection uses `return default!` not `throw` — avoids triggering the error counter path and keeps pipeline overhead minimal for rejected-but-not-exceptional events.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. Build succeeded with zero errors and zero warnings on both tasks.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ValidationBehavior and OidResolutionBehavior are complete and compile cleanly
- Pipeline order for DI registration: LoggingBehavior (outermost) → ValidationBehavior → OidResolutionBehavior → PublishBehavior (innermost, Phase 03-04)
- Both behaviors expect constructor injection: ValidationBehavior needs ILogger, PipelineMetricService, IDeviceRegistry; OidResolutionBehavior needs IOidMapService
- Ready for 03-04 (handler/publish behavior) and 03-06 (DI registration in correct order)

---
*Phase: 03-mediatr-pipeline-and-instruments*
*Completed: 2026-03-05*
