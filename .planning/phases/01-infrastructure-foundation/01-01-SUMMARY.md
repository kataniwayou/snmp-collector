---
phase: 01-infrastructure-foundation
plan: 01
subsystem: infra
tags: [dotnet, csharp, generic-host, otel, quartz, configuration]

# Dependency graph
requires: []
provides:
  - .NET 9 Generic Host project scaffold (SnmpCollector.csproj, Microsoft.NET.Sdk)
  - Four typed configuration options classes with SectionName constants and data annotations
  - Three appsettings files (base, Development, Production)
  - GlobalUsings.cs scoping SnmpCollector.Configuration
  - Minimal Program.cs entry point using Host.CreateApplicationBuilder
affects:
  - 01-02 (telemetry bootstrap)
  - 01-03 (SNMP listener)
  - 01-04 (Quartz scheduling)
  - 01-05 (DI wiring and startup validation)
  - All subsequent phases that depend on this compilable shell

# Tech tracking
tech-stack:
  added:
    - Microsoft.Extensions.Hosting 9.0.0
    - OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0
    - OpenTelemetry.Extensions.Hosting 1.15.0
    - OpenTelemetry.Instrumentation.Runtime 1.15.0
    - Quartz.Extensions.Hosting 3.15.1
  patterns:
    - "File-scoped namespaces (namespace SnmpCollector.X;)"
    - "Sealed options classes with SectionName constants and data annotations"
    - "required keyword for mandatory config fields with [Required] annotation"
    - "Generic Host (not Web Host) — Microsoft.NET.Sdk, not Microsoft.NET.Sdk.Web"

key-files:
  created:
    - src/SnmpCollector/SnmpCollector.csproj
    - src/SnmpCollector/GlobalUsings.cs
    - src/SnmpCollector/Program.cs
    - src/SnmpCollector/Configuration/SiteOptions.cs
    - src/SnmpCollector/Configuration/OtlpOptions.cs
    - src/SnmpCollector/Configuration/LoggingOptions.cs
    - src/SnmpCollector/Configuration/CorrelationJobOptions.cs
    - src/SnmpCollector/appsettings.json
    - src/SnmpCollector/appsettings.Development.json
    - src/SnmpCollector/appsettings.Production.json
  modified: []

key-decisions:
  - "Use Microsoft.NET.Sdk (Generic Host), not Microsoft.NET.Sdk.Web — SnmpCollector has no HTTP server"
  - "Use Quartz.Extensions.Hosting (not Quartz.AspNetCore) — correct package for Generic Host"
  - "SiteOptions.Role string (default standalone) replaces ILeaderElection in Phase 1 — Phase 7 adds dynamic role"
  - "Microsoft.Extensions.Hosting 9.0.0 added as explicit dependency — non-Web SDK requires it for Host.CreateApplicationBuilder"
  - "OtlpOptions.ServiceName defaults to snmp-collector (not simetra-supervisor)"

patterns-established:
  - "SectionName constant pattern: every options class has public const string SectionName = '...' for binding"
  - "All options classes are public sealed with file-scoped namespace"
  - "GlobalUsings.cs provides single global using for Configuration namespace"
  - "appsettings.json holds full skeleton including future-phase placeholder sections (Devices, OidMap, SnmpListener)"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 1 Plan 01: Project Scaffold Summary

**.NET 9 Generic Host project scaffold with four typed configuration options classes (SiteOptions, OtlpOptions, LoggingOptions, CorrelationJobOptions), pinned OTel 1.15.0 / Quartz 3.15.1 packages, and three-tier appsettings JSON — compilable with zero warnings**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-04T22:39:41Z
- **Completed:** 2026-03-04T22:41:56Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments

- Buildable SnmpCollector project targeting net9.0 using Microsoft.NET.Sdk (Generic Host, not Web Host)
- Four typed configuration options classes with SectionName constants, data annotations, and correct defaults — binding contract established for all subsequent plans
- Three appsettings files with full skeleton including future-phase placeholder sections (Devices, OidMap, SnmpListener)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create project file and GlobalUsings** - `1224c0f` (feat)
2. **Task 2: Create configuration options classes** - `10a44be` (feat)
3. **Task 3: Create appsettings files** - `7c8fbe0` (feat)

## Files Created/Modified

- `src/SnmpCollector/SnmpCollector.csproj` - Generic Host project targeting net9.0 with 5 pinned NuGet packages
- `src/SnmpCollector/GlobalUsings.cs` - Global using for SnmpCollector.Configuration namespace
- `src/SnmpCollector/Program.cs` - Minimal Generic Host entry point stub
- `src/SnmpCollector/Configuration/SiteOptions.cs` - Site identification (required Name, Role defaulting to "standalone")
- `src/SnmpCollector/Configuration/OtlpOptions.cs` - OTLP exporter (required Endpoint, ServiceName defaulting to "snmp-collector")
- `src/SnmpCollector/Configuration/LoggingOptions.cs` - Console logging toggle (EnableConsole bool)
- `src/SnmpCollector/Configuration/CorrelationJobOptions.cs` - Job timing (IntervalSeconds, range 1..MaxValue, default 30)
- `src/SnmpCollector/appsettings.json` - Full config skeleton with all sections
- `src/SnmpCollector/appsettings.Development.json` - EnableConsole:true, LogLevel:Debug, Otlp endpoint localhost:4317
- `src/SnmpCollector/appsettings.Production.json` - Empty placeholder

## Decisions Made

- **Generic Host, not Web Host:** SnmpCollector has no HTTP surface — `Microsoft.NET.Sdk` is the correct SDK. Using `Microsoft.NET.Sdk.Web` would pull in ASP.NET Core pipeline unnecessarily.
- **Quartz.Extensions.Hosting vs Quartz.AspNetCore:** Plan explicitly required the Generic Host package. Quartz.AspNetCore adds Web-specific integration that would conflict.
- **SiteOptions.Role = "standalone":** Phase 1 has no leader election. The static `Role` string is read by Phase 2's formatter and enrichment processor. Phase 7 will make it dynamic.
- **OtlpOptions.ServiceName default:** Changed from `simetra-supervisor` to `snmp-collector` to correctly identify this service in telemetry backends.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added minimal Program.cs stub**
- **Found during:** Task 2 (build verification)
- **Issue:** Project is `OutputType=Exe` — compiler requires an entry point. Plan did not list Program.cs but the project cannot compile without one.
- **Fix:** Added a minimal `Program.cs` using `Host.CreateApplicationBuilder` pattern that subsequent plans will expand.
- **Files modified:** `src/SnmpCollector/Program.cs`
- **Verification:** `dotnet build` succeeds with zero errors after addition.
- **Committed in:** `10a44be` (Task 2 commit)

**2. [Rule 3 - Blocking] Added Microsoft.Extensions.Hosting 9.0.0 package reference**
- **Found during:** Task 2 (build verification)
- **Issue:** `Microsoft.NET.Sdk` (non-Web) does not include `Microsoft.Extensions.Hosting` as an implicit using or transitive package. `Host.CreateApplicationBuilder` is defined there. Build error: CS0103 "The name 'Host' does not exist in the current context".
- **Fix:** Added `<PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.0" />` to the project file.
- **Files modified:** `src/SnmpCollector/SnmpCollector.csproj`
- **Verification:** `dotnet build` succeeds with zero errors after addition.
- **Committed in:** `10a44be` (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both fixes mandatory for the project to compile. No scope creep — Program.cs is the minimum entry point stub that subsequent plans will expand.

## Issues Encountered

- Non-Web SDK implicit usings do not include `Microsoft.Extensions.Hosting` — required explicit package reference. This is expected behavior for a Generic Host project and not a recurring concern once established.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Project compiles with zero warnings at net9.0 targeting Generic Host pattern
- Four config options classes define the binding contract for appsettings JSON — all subsequent plans can bind to these directly
- Program.cs stub is ready for service registrations (Plans 01-02 through 01-05 each add their registrations)
- appsettings.json skeleton includes placeholder sections (Devices, OidMap, SnmpListener) so later plans can populate them without JSON schema changes

---
*Phase: 01-infrastructure-foundation*
*Completed: 2026-03-05*
