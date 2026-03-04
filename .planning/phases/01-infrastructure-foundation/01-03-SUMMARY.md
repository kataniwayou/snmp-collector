---
phase: 01-infrastructure-foundation
plan: 03
subsystem: infra
tags: [correlation, telemetry, logging, opentelemetry, quartz, console-formatter, otlp]

# Dependency graph
requires:
  - phase: 01-infrastructure-foundation/01-01
    provides: Project scaffold with SiteOptions, OtlpOptions, QuartzOptions configuration classes and build

provides:
  - ICorrelationService interface (CurrentCorrelationId, OperationCorrelationId, SetCorrelationId)
  - RotatingCorrelationService with volatile + static AsyncLocal concurrency model
  - CorrelationJob Quartz job for periodic correlation ID rotation
  - TelemetryConstants with single MeterName = "SnmpCollector"
  - SnmpConsoleFormatter with dual correlation ID display and SiteOptions.Role resolution
  - SnmpLogEnrichmentProcessor for OTLP log enrichment with site/role/correlationId

affects:
  - 01-04 (DI extension methods will wire all these classes)
  - 01-05 (Program.cs will call those extensions)
  - Phase 3 (all metric instruments use TelemetryConstants.MeterName)
  - Phase 5 (trap pipeline will capture OperationCorrelationId per async context)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Lazy DI resolution via PostConfigureOptions ServiceProvider injection (ConsoleFormatter cannot use constructor DI)"
    - "Volatile string + static AsyncLocal for single-writer/multi-reader correlation ID"
    - "Static role string in Phase 1; Func<string> deferred to Phase 7 leader election"

key-files:
  created:
    - src/SnmpCollector/Pipeline/ICorrelationService.cs
    - src/SnmpCollector/Pipeline/RotatingCorrelationService.cs
    - src/SnmpCollector/Jobs/CorrelationJob.cs
    - src/SnmpCollector/Telemetry/TelemetryConstants.cs
    - src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs
    - src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs
  modified: []

key-decisions:
  - "SnmpConsoleFormatter shows BOTH globalId and operationId (Simetra showed either/or)"
  - "SnmpLogEnrichmentProcessor takes string role not Func<string> -- static in Phase 1, dynamic in Phase 7"
  - "CorrelationJob finally block removed entirely -- no liveness vector in Phase 1"
  - "TelemetryConstants has single MeterName (Simetra had LeaderMeterName/InstanceMeterName split)"
  - "Microsoft.Extensions.DependencyInjection using required for GetService<T> extension on IServiceProvider"

patterns-established:
  - "Dual correlation ID: globalId always shown, operationId appended when async context captures it"
  - "PostConfigure pattern: formatter options hold IServiceProvider, resolved lazily on first Write"
  - "AsyncLocal must be static for correct async context flow -- instance AsyncLocal would not propagate"

# Metrics
duration: 5min
completed: 2026-03-05
---

# Phase 1 Plan 03: Correlation Service and Telemetry Classes Summary

**RotatingCorrelationService (volatile + static AsyncLocal) with dual-ID SnmpConsoleFormatter showing globalId|operationId, and OTLP SnmpLogEnrichmentProcessor using static role string**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-03-04T22:44:34Z
- **Completed:** 2026-03-04T22:49:00Z
- **Tasks:** 2
- **Files modified:** 6 created

## Accomplishments

- Correlation service contract and lock-free implementation (volatile write, static AsyncLocal per-async-context)
- CorrelationJob cleaned of ILivenessVectorService — rotates IDs on schedule without Phase 1 liveness dependency
- SnmpConsoleFormatter upgraded over Simetra: shows both globalId AND operationId (not either/or), reads role from SiteOptions.Role instead of ILeaderElection
- SnmpLogEnrichmentProcessor simplified: takes string role directly instead of Func<string> delegate
- TelemetryConstants with single MeterName constant for Phase 3 metric instruments

## Task Commits

Each task was committed atomically:

1. **Task 1: Create correlation service, CorrelationJob, and TelemetryConstants** - `98a3466` (feat)
2. **Task 2: Create console formatter and log enrichment processor** - `b424cbc` (feat)

**Plan metadata:** (pending this commit) (docs: complete plan)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/ICorrelationService.cs` - Three-member interface: CurrentCorrelationId, OperationCorrelationId, SetCorrelationId
- `src/SnmpCollector/Pipeline/RotatingCorrelationService.cs` - Volatile string field + static AsyncLocal; lock-free single-writer/multi-reader
- `src/SnmpCollector/Jobs/CorrelationJob.cs` - Quartz IJob with [DisallowConcurrentExecution], no ILivenessVectorService
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` - Single MeterName = "SnmpCollector" constant
- `src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs` - Custom formatter: dual correlation ID, SiteOptions.Role, lazy DI resolution, 3-char level abbreviations
- `src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs` - BaseProcessor<LogRecord> enriching with site_name, role, correlationId

## Decisions Made

- SnmpConsoleFormatter shows BOTH globalId and operationId. Simetra showed operationId OR globalId (operationId ?: globalId). The SnmpCollector formatter always shows globalId and appends operationId when the async context has one set. This gives full context in logs.
- SnmpLogEnrichmentProcessor takes `string role` not `Func<string>`. Phase 1 role is static from SiteOptions. Phase 7 will refactor to `Func<string>` when leader election makes role dynamic.
- TelemetryConstants is a single constant. Simetra split LeaderMeterName/InstanceMeterName for role-gated metric export. Phase 1 has no gating, so one MeterName suffices.
- CorrelationJob finally block removed entirely. The only code it contained was `_liveness.Stamp(jobKey)` which requires ILivenessVectorService. Without that dependency, the finally block has no purpose.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added Microsoft.Extensions.DependencyInjection using for GetService<T>**
- **Found during:** Task 2 (SnmpConsoleFormatter)
- **Issue:** `sp.GetService<ICorrelationService>()` and `sp.GetService<IOptions<SiteOptions>>()` failed with CS0308 -- "non-generic method IServiceProvider.GetService(Type) cannot be used with type arguments". The `GetService<T>()` generic overload lives in `Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions`, which is not imported by default.
- **Fix:** Added `using Microsoft.Extensions.DependencyInjection;` to the formatter file
- **Files modified:** src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs
- **Verification:** `dotnet build` succeeded with 0 errors after fix
- **Committed in:** b424cbc (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Required for compilation. No scope creep -- the Simetra source used this same extension method via transitive using in their project structure.

## Issues Encountered

None beyond the missing using statement auto-fixed above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All six source files compile and verified
- Plan 01-04 (DI extension methods) can now wire: `services.AddSingleton<ICorrelationService, RotatingCorrelationService>()`, `services.AddCustomConsoleFormatter<SnmpConsoleFormatter, SnmpConsoleFormatterOptions>()`, `services.AddSingleton<PostConfigureSnmpFormatterOptions>()`, and `services.AddOpenTelemetry().WithLogging(b => b.AddProcessor(new SnmpLogEnrichmentProcessor(...)))`
- Plan 01-05 (Program.cs) will call the extension methods
- No blockers

---
*Phase: 01-infrastructure-foundation*
*Completed: 2026-03-05*
