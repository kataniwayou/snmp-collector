---
phase: 01-infrastructure-foundation
plan: 04
subsystem: infra
tags: [dotnet, csharp, opentelemetry, otlp, quartz, di, generic-host, logging, metrics, configuration]

# Dependency graph
requires:
  - phase: 01-infrastructure-foundation/01-01
    provides: Project scaffold, four options classes (SiteOptions, OtlpOptions, LoggingOptions, CorrelationJobOptions), appsettings files
  - phase: 01-infrastructure-foundation/01-03
    provides: ICorrelationService, RotatingCorrelationService, CorrelationJob, SnmpConsoleFormatter, SnmpLogEnrichmentProcessor, TelemetryConstants

provides:
  - ServiceCollectionExtensions with AddSnmpTelemetry, AddSnmpConfiguration, AddSnmpScheduling
  - AddSnmpTelemetry: OTel MeterProvider with direct OTLP metric export, conditional SnmpConsoleFormatter, OTLP log exporter with SnmpLogEnrichmentProcessor
  - AddSnmpConfiguration: four options classes bound with ValidateDataAnnotations + ValidateOnStart
  - AddSnmpScheduling: ICorrelationService singleton + Quartz in-memory store + CorrelationJob with RepeatForever trigger
  - Program.cs: Generic Host entry point with DI registration order Telemetry->Config->Scheduling, correlation seed, fail-fast OptionsValidationException catch
  - Running application verified: structured console output with {timestamp} [{level}] [{site}|{role}|{globalId}] {category} {message}

affects:
  - 01-05 (custom IValidateOptions validators plug into AddSnmpConfiguration)
  - Phase 3 (SNMP pipeline services will add methods to ServiceCollectionExtensions)
  - Phase 6 (state/metric poll jobs added to AddSnmpScheduling)
  - Phase 7 (leader election added to AddSnmpTelemetry, role becomes Func<string>)
  - Phase 8 (heartbeat job added to AddSnmpScheduling)

# Tech tracking
tech-stack:
  added:
    - Microsoft.Extensions.Options.DataAnnotations 9.0.0
  patterns:
    - "Direct config binding before DI container builds: new OtlpOptions { Endpoint = '', ... }; builder.Configuration.Bind()"
    - "Blank endpoint guard: fallback URI prevents UriFormatException; ValidateOnStart catches missing config with clear error"
    - "Logging enrichment processor factory lambda resolves IOptions<SiteOptions> and ICorrelationService at DI resolution time"
    - "ICorrelationService registered before AddQuartz so CorrelationJob DI resolution succeeds"

key-files:
  created:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
  modified:
    - src/SnmpCollector/Program.cs
    - src/SnmpCollector/SnmpCollector.csproj

key-decisions:
  - "No WithTracing in AddSnmpTelemetry (LOG-07: SnmpCollector emits no distributed traces)"
  - "Direct AddOtlpExporter on metrics (no MetricRoleGatedExporter wrapper -- Phase 1 has no leader election)"
  - "OtlpOptions required members initialized with empty defaults before Bind() to satisfy C# required keyword"
  - "Microsoft.Extensions.Options.DataAnnotations 9.0.0 added -- ValidateDataAnnotations() extension is not in base Options package"
  - "OptionsValidationException catch wraps host.RunAsync() not builder.Build() -- ValidateOnStart fires during RunAsync host startup sequence"

patterns-established:
  - "Three-method DI extension pattern: AddSnmpTelemetry(IHostApplicationBuilder), AddSnmpConfiguration(IServiceCollection, IConfiguration), AddSnmpScheduling(IServiceCollection, IConfiguration)"
  - "Registration order enforced by comment in Program.cs: Telemetry first = disposed last = ForceFlush on shutdown"
  - "Correlation seed placement: after builder.Build(), before host.RunAsync() -- after DI container built, before any hosted service fires"

# Metrics
duration: 5min
completed: 2026-03-05
---

# Phase 1 Plan 04: DI Extension Methods and Program.cs Summary

**Three DI extension methods wiring all Phase 1 classes (OTel direct OTLP metrics, conditional SnmpConsoleFormatter, OTLP log enrichment) into a running Generic Host that logs `{timestamp} [{level}] [{site}|{role}|{globalId}] {category} {message}` to console**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-03-04T22:49:00Z
- **Completed:** 2026-03-04T22:54:00Z
- **Tasks:** 2
- **Files modified:** 3 (1 created, 2 modified)

## Accomplishments

- `ServiceCollectionExtensions.cs` with three methods connecting all Phase 1 building blocks: AddSnmpTelemetry wires OTel MeterProvider with direct OTLP export + conditional console formatter + OTLP log enrichment; AddSnmpConfiguration registers all four options classes with fail-fast validation; AddSnmpScheduling registers correlation service and Quartz job
- `Program.cs` using Generic Host (`Host.CreateApplicationBuilder`), correct DI registration order, correlation ID seeded before any Quartz job can fire, and OptionsValidationException catch for human-readable validation failures
- Application verified running: console shows structured log lines with site/role/correlationId in every record

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ServiceCollectionExtensions with three DI methods** - `223b8cd` (feat)
2. **Task 2: Create Program.cs entry point** - `7752fb2` (feat)

**Plan metadata:** (pending this commit) (docs: complete plan)

## Files Created/Modified

- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Three static extension methods: AddSnmpTelemetry (OTel metrics + logging), AddSnmpConfiguration (4 options with ValidateOnStart), AddSnmpScheduling (correlation singleton + Quartz in-memory)
- `src/SnmpCollector/Program.cs` - Generic Host entry point with registration order comment, correlation seed, fail-fast exception handler
- `src/SnmpCollector/SnmpCollector.csproj` - Added Microsoft.Extensions.Options.DataAnnotations 9.0.0

## Decisions Made

- **No WithTracing:** LOG-07 — SnmpCollector emits no distributed traces. Removed entirely from AddSnmpTelemetry vs Simetra which had a full `WithTracing` block.
- **Direct OTLP metric export:** Phase 1 has no leader election so no `MetricRoleGatedExporter` wrapper. All instances export directly via `AddOtlpExporter()`.
- **OtlpOptions required member initialization:** C# `required` keyword requires `Endpoint` and `ServiceName` to be set in object initializer. Used `{ Endpoint = "", ServiceName = "snmp-collector" }` as defaults before `Bind()` overwrites with actual config values. This matches Simetra's explicit initialization pattern.
- **Microsoft.Extensions.Options.DataAnnotations package required:** `ValidateDataAnnotations()` extension lives in a separate package (`Microsoft.Extensions.Options.DataAnnotations`), not in the base `Microsoft.Extensions.Options`. Added 9.0.0.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added missing using directives to ServiceCollectionExtensions.cs**
- **Found during:** Task 1 (build verification)
- **Issue:** `IHostApplicationBuilder`, `IServiceCollection`, `IConfiguration`, `ILoggingBuilder` not in scope. Generic Host project GlobalUsings.cs only has `SnmpCollector.Configuration`. These types need explicit usings.
- **Fix:** Added `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging` usings
- **Files modified:** `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs`
- **Verification:** `dotnet build` succeeded with 0 errors after fix
- **Committed in:** `223b8cd` (Task 1 commit)

**2. [Rule 3 - Blocking] Added Microsoft.Extensions.Options.DataAnnotations package**
- **Found during:** Task 1 (build verification)
- **Issue:** `ValidateDataAnnotations()` extension method not found -- it lives in `Microsoft.Extensions.Options.DataAnnotations`, not the base Options package.
- **Fix:** `dotnet add package Microsoft.Extensions.Options.DataAnnotations --version 9.0.0`
- **Files modified:** `src/SnmpCollector/SnmpCollector.csproj`
- **Verification:** `dotnet build` succeeded after package added
- **Committed in:** `223b8cd` (Task 1 commit)

**3. [Rule 3 - Blocking] Added missing using directives to Program.cs**
- **Found during:** Task 2 (build verification)
- **Issue:** `Host` not in scope (needs `Microsoft.Extensions.Hosting`) and `GetRequiredService<T>` generic extension not in scope (needs `Microsoft.Extensions.DependencyInjection`). GlobalUsings.cs doesn't import either.
- **Fix:** Added both using directives at top of Program.cs
- **Files modified:** `src/SnmpCollector/Program.cs`
- **Verification:** `dotnet build` succeeded with 0 errors after fix
- **Committed in:** `7752fb2` (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (3 blocking)
**Impact on plan:** All fixes required for compilation. Missing usings are a direct consequence of the Generic Host project not using implicit `Microsoft.Extensions.*` usings (non-Web SDK). No scope creep.

## Issues Encountered

- `ValidateDataAnnotations()` requires a separate NuGet package, not included transitively from `Microsoft.Extensions.Hosting`. This is the same pattern hit in Plan 01-01 where `Microsoft.Extensions.Hosting` itself needed explicit addition.
- First `dotnet run` test (without `DOTNET_ENVIRONMENT=Development`) showed OptionsValidationException on `SiteOptions.Name` -- this was expected behavior. The base `appsettings.json` has the Site.Name value; the test simply had the env var passed incorrectly as a CLI arg instead of environment variable. Confirmed working with correct env var approach.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All Phase 1 DI wiring complete: extension methods connect all six Phase 1 classes into a running host
- `dotnet run` with `DOTNET_ENVIRONMENT=Development` produces structured console output: `{timestamp} [{level}] [{site}|{role}|{globalId}] {category} {message}`
- Plan 01-05 will add custom `IValidateOptions<T>` validator classes and register them inside `AddSnmpConfiguration`
- The `AddSnmpScheduling` method is ready for future jobs (heartbeat Phase 8, state/metric poll Phase 6) to be added inside the `AddQuartz(q => { ... })` lambda
- No blockers

---
*Phase: 01-infrastructure-foundation*
*Completed: 2026-03-05*
