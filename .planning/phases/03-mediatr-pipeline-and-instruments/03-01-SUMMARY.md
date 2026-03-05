---
phase: 03-mediatr-pipeline-and-instruments
plan: 01
subsystem: pipeline
tags: [mediatr, snmp, sharpsnmplib, otel, metrics, notifications, counters]

# Dependency graph
requires:
  - phase: 01-infrastructure-foundation
    provides: TelemetryConstants.MeterName, IMeterFactory via OTel hosting, SiteOptions
  - phase: 02-device-registry-and-oid-map
    provides: SiteOptions.Name used as site_name label in all pipeline counters
provides:
  - MediatR 12.5.0 NuGet package in SnmpCollector.csproj (MIT license)
  - Lextm.SharpSnmpLib 12.5.7 in SnmpCollector.csproj and SnmpCollector.Tests.csproj
  - SnmpOidReceived sealed INotification class — contract all behaviors and handlers operate on
  - SnmpSource enum (Poll, Trap) for dispatch discrimination
  - PipelineMetricService singleton owning all 6 pipeline counter instruments
affects:
  - 03-02-PLAN.md (OidResolutionBehavior — publishes SnmpOidReceived, uses PipelineMetricService)
  - 03-03-PLAN.md (ErrorLoggingBehavior — wraps SnmpOidReceived pipeline)
  - 03-04-PLAN.md (SNMP metric handlers — consume SnmpOidReceived)
  - 03-05-PLAN.md (Poll job — constructs and publishes SnmpOidReceived)
  - 03-06-PLAN.md (Trap listener — constructs and publishes SnmpOidReceived)
  - All future phases using MediatR pipeline or SNMP notification model

# Tech tracking
tech-stack:
  added:
    - MediatR 12.5.0 (MIT license — locked, do not upgrade to v13+ RPL-1.5)
    - Lextm.SharpSnmpLib 12.5.7
  patterns:
    - MediatR INotification sealed class for pipeline events (not records — mutable for behavior enrichment)
    - PipelineMetricService singleton pattern — create all instruments once, inject everywhere
    - TagList { { "site_name", _siteName } } pattern for all pipeline counter increments

key-files:
  created:
    - src/SnmpCollector/Pipeline/SnmpOidReceived.cs
    - src/SnmpCollector/Pipeline/SnmpSource.cs
    - src/SnmpCollector/Telemetry/PipelineMetricService.cs
  modified:
    - src/SnmpCollector/SnmpCollector.csproj
    - tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj

key-decisions:
  - "MediatR 12.5.0 MIT license — locked from Init, never upgrade to v13+ RPL-1.5"
  - "SnmpOidReceived is a sealed class not a record — behaviors enrich properties in-place via set"
  - "AgentIp uses set not init — trap path may update address post-construction"
  - "System.Diagnostics using required for TagList alongside System.Diagnostics.Metrics"
  - "PipelineMetricService takes IMeterFactory not Meter directly — follows OTel hosting pattern"

patterns-established:
  - "Pipeline notification pattern: sealed class implementing INotification with required init properties and nullable enrichment properties"
  - "Counter increment pattern: counter.Add(1, new TagList { { \"site_name\", _siteName } })"
  - "Metric service singleton: create Meter via IMeterFactory, all counters in constructor"

# Metrics
duration: 2min 10sec
completed: 2026-03-05
---

# Phase 3 Plan 1: NuGet Foundation and Pipeline Contract Summary

**MediatR 12.5.0 + SharpSnmpLib 12.5.7 added; SnmpOidReceived INotification contract, SnmpSource enum, and PipelineMetricService with all 6 pipeline counters established as Phase 3 foundation**

## Performance

- **Duration:** 2 min 10 sec
- **Started:** 2026-03-05T01:32:05Z
- **Completed:** 2026-03-05T01:34:15Z
- **Tasks:** 2
- **Files modified:** 5 (2 csproj updated, 3 source files created)

## Accomplishments

- Added MediatR 12.5.0 (MIT, version-locked) and SharpSnmpLib 12.5.7 to SnmpCollector project; SharpSnmpLib 12.5.7 also added to test project for SNMP data construction in tests
- Created SnmpOidReceived sealed class implementing INotification with all 7 properties (Oid, AgentIp, DeviceName, Value, Source, TypeCode, MetricName) — the typed contract that all Phase 3 behaviors and handlers operate on
- Created PipelineMetricService singleton with all 6 pipeline counters (snmp.event.published, .handled, .errors, .rejected; snmp.poll.executed, snmp.trap.received) using site_name tag on every Add call; both projects build with zero errors

## Task Commits

Each task was committed atomically:

1. **Task 1: Add MediatR 12.5.0 and SharpSnmpLib 12.5.7 NuGet packages** - `8f921fe` (chore)
2. **Task 2: Create SnmpOidReceived notification, SnmpSource enum, and PipelineMetricService** - `e5df5d1` (feat)

**Plan metadata:** *(pending docs commit)*

## Files Created/Modified

- `src/SnmpCollector/SnmpCollector.csproj` - Added MediatR 12.5.0 and Lextm.SharpSnmpLib 12.5.7 PackageReferences
- `tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` - Added Lextm.SharpSnmpLib 12.5.7 PackageReference
- `src/SnmpCollector/Pipeline/SnmpSource.cs` - Enum with Poll and Trap values for dispatch discrimination
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` - Sealed INotification with Oid, AgentIp (set), DeviceName?, Value (ISnmpData), Source, TypeCode (SnmpType), MetricName?
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` - Singleton owning 6 Counter<long> instruments; IMeterFactory + IOptions<SiteOptions> constructor

## Decisions Made

- MediatR 12.5.0 is version-locked (MIT license). Version 13+ switched to RPL-1.5 commercial license — this constraint came from project Init and is immutable.
- SnmpOidReceived is a `sealed class` not a `record` — behaviors (OidResolutionBehavior, etc.) enrich properties in-place. Records are immutable by convention; using a class signals intentional mutability.
- `AgentIp` uses `set` (not `init`) — the trap receive path may update the sender IP address after the notification object is constructed.
- `PipelineMetricService` takes `IMeterFactory` (not `Meter` directly) — follows the OTel .NET hosting pattern where the factory manages meter lifetime tied to the DI container.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added missing `using System.Diagnostics;` for TagList**

- **Found during:** Task 2 (PipelineMetricService implementation)
- **Issue:** `TagList` struct is in the `System.Diagnostics` namespace (from `System.Diagnostics.DiagnosticSource`) but `using System.Diagnostics.Metrics;` alone does not bring it in scope. Build failed with CS0246 on all 6 counter Add calls.
- **Fix:** Added `using System.Diagnostics;` to PipelineMetricService.cs alongside `using System.Diagnostics.Metrics;`. The `System.Diagnostics.DiagnosticSource` package is already a transitive dependency via OpenTelemetry packages.
- **Files modified:** `src/SnmpCollector/Telemetry/PipelineMetricService.cs`
- **Verification:** `dotnet build src/SnmpCollector/SnmpCollector.csproj -c Release` — 0 errors, 0 warnings
- **Committed in:** `e5df5d1` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug: missing using directive)
**Impact on plan:** Auto-fix necessary for compilation. No scope creep. The plan's code pattern was correct; only the using import was missing.

## Issues Encountered

None beyond the TagList using directive documented above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 3 Plan 2 (OidResolutionBehavior) can proceed immediately — SnmpOidReceived contract and MediatR package are in place
- Phase 3 Plan 3 (ErrorLoggingBehavior), Plan 4 (handlers), Plan 5 (poll job), Plan 6 (trap listener) all depend on this plan's artifacts and are unblocked
- Both SnmpCollector.csproj and SnmpCollector.Tests.csproj build cleanly with zero errors after package additions

---
*Phase: 03-mediatr-pipeline-and-instruments*
*Completed: 2026-03-05*
