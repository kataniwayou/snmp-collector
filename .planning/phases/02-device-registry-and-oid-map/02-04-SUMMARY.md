---
phase: 02-device-registry-and-oid-map
plan: 04
subsystem: pipeline
tags: [dotnet, IHostedLifecycleService, cardinality, observability, startup-audit, DI]

# Dependency graph
requires:
  - phase: 02-device-registry-and-oid-map/02-02
    provides: IDeviceRegistry (AllDevices), IOidMapService (EntryCount, Resolve), DeviceInfo/MetricPollInfo runtime records
  - phase: 01-infrastructure-foundation/01-04
    provides: ServiceCollectionExtensions.AddSnmpConfiguration, AddHostedService pattern
provides:
  - CardinalityAuditService: IHostedLifecycleService running StartingAsync before Quartz, computing devices x OIDs x instruments x sources estimate
  - Cardinality gate for Phase 3: startup log of estimated metric series count (Information) + warning at >10,000 series
  - Label taxonomy documentation-in-logs: site_name, metric_name, oid, agent, source with bounded values
  - AddHostedService<CardinalityAuditService> registered in AddSnmpConfiguration
affects:
  - Phase 3 (OTel instruments): cardinality gate satisfied, safe to create instruments
  - Phase 8 (operations): label taxonomy and series estimate visible in Grafana log panels at startup

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "IHostedLifecycleService.StartingAsync: fires before IHostedService.StartAsync -- use for pre-startup audits that must complete before Quartz begins"
    - "Warn-but-allow: log Warning without throwing or blocking startup; operator-visible without halting service"
    - "Cardinality formula: devices x max(OID map entries, unique poll OIDs) x InstrumentCount x SourceCount"
    - "Documentation-in-logs: label taxonomy emitted at startup Information so operators see bounded label values without consulting docs"

key-files:
  created:
    - src/SnmpCollector/Pipeline/CardinalityAuditService.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "IHostedLifecycleService used (not IHostedService) -- StartingAsync fires before StartAsync, ensuring audit completes before Quartz scheduler begins executing jobs"
  - "WarningThreshold = 10_000 series -- from Phase 2 RESEARCH.md; Prometheus performance degrades above this bound for a single scrape target"
  - "max(oidMapEntries, uniquePollOids) for OID dimension -- traps may reference OIDs not covered by any poll group; OID map is the upper bound of reachable metric names"

patterns-established:
  - "Audit service pattern: sealed IHostedLifecycleService with StartingAsync that calls synchronous audit helper -- clean separation of interface ceremony from logic"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 2 Plan 4: CardinalityAuditService Summary

**IHostedLifecycleService startup audit computing devices x OIDs x 3 instruments x 2 sources cardinality estimate with warn-but-allow at >10,000 series -- closes Phase 2 cardinality gate for Phase 3**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-05T00:19:08Z
- **Completed:** 2026-03-05T00:21:00Z
- **Tasks:** 1
- **Files modified:** 2 (1 created, 1 modified)

## Accomplishments

- Created `CardinalityAuditService` implementing `IHostedLifecycleService`; `StartingAsync` runs before Quartz starts any jobs, making it the first operator-visible output at startup
- Cardinality formula uses `max(oidMapEntries, uniquePollOids)` as the OID dimension (traps may reference OIDs absent from poll groups -- OID map is the upper bound)
- Logs two Information lines at startup: one with the numeric estimate (devices / OID map entries / unique poll OIDs / instruments / sources / total), one with the label taxonomy and bounded values
- Warns at Warning level if estimate exceeds 10,000 series; does not throw or block startup (warn-but-allow)
- Registered via `AddHostedService<CardinalityAuditService>()` at the end of `AddSnmpConfiguration`

## Task Commits

Each task was committed atomically:

1. **Task 1: Create CardinalityAuditService and register it** - `12b79ef` (feat)

**Plan metadata:** (committed after SUMMARY creation)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/CardinalityAuditService.cs` - Sealed `IHostedLifecycleService`; `StartingAsync` computes cardinality estimate and logs at Information; warns at Warning if >10,000; all other lifecycle methods return `Task.CompletedTask`
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Added `services.AddHostedService<CardinalityAuditService>()` at end of Phase 2 pipeline singleton block in `AddSnmpConfiguration`

## Decisions Made

- `IHostedLifecycleService` chosen over `IHostedService` -- the `StartingAsync` callback fires before `StartAsync`, which means it runs before the `QuartzHostedService` begins scheduling jobs. Using `IHostedService.StartAsync` would race with Quartz startup.
- `WarningThreshold = 10_000` -- from Phase 2 RESEARCH.md; cited as the Prometheus series bound above which per-target scrape latency degrades noticeably on default storage engine.
- OID dimension uses `Math.Max(oidMapEntries, uniquePollOids)` -- traps may send OIDs that no poll group covers. The OID map defines all known resolvable metric names, so it is the correct upper bound for the OID label cardinality.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Killed running SnmpCollector process that held file lock**

- **Found during:** Task 1 verification (dotnet build)
- **Issue:** A previous `SnmpCollector` process (PID 27872) was still running, holding an exclusive lock on `bin\Debug\net9.0\SnmpCollector.exe`. Build failed with MSB3027 after 10 retries.
- **Fix:** Ran `powershell Stop-Process -Id 27872 -Force` to terminate the locked process, then rebuilt successfully.
- **Files modified:** None (execution procedure only)
- **Verification:** `dotnet build` returned `Build succeeded. 0 Warning(s) 0 Error(s)` after termination.
- **Committed in:** N/A (no file changes)

---

**Total deviations:** 1 auto-fixed (1 blocking -- process lock)
**Impact on plan:** No scope creep. File lock is a recurring Windows dev environment characteristic (previously documented in 02-02-SUMMARY.md Issues Encountered); addressed the same way.

## Issues Encountered

**Build file-lock (recurring Windows dev environment issue):** `dotnet build` fails with MSB3027 when SnmpCollector.exe is still running from a previous session. Terminated with `powershell Stop-Process -Id 27872 -Force`. Zero `error CS` compile errors; build clean after termination.

## User Setup Required

None -- `CardinalityAuditService` requires no new config keys. The audit reads existing `IDeviceRegistry` and `IOidMapService` state built from `appsettings.Development.json`.

## Next Phase Readiness

- Phase 2 Success Criterion #4 satisfied: label taxonomy documented (site_name, metric_name, oid, agent, source) with bounded values; cardinality estimate logged at startup
- Phase 3 (MediatR pipeline / OTel instruments) can proceed -- the cardinality gate is in place and operators will see the series count before any instruments are created in production
- For Development config (2 devices, 5 OIDs): estimated series = 2 x 5 x 3 x 2 = 60, well below 10,000 threshold; no warning emitted

---
*Phase: 02-device-registry-and-oid-map*
*Completed: 2026-03-05*
