---
phase: 15-k8s-configmap-watch-and-unified-config
plan: 03
subsystem: configuration
tags: [di-wiring, program-startup, unified-config, oidmap, local-dev]
dependency-graph:
  requires:
    - phase: 15-01
      provides: mutable OidMapService and DeviceRegistry with UpdateMap/ReloadAsync
    - phase: 15-02
      provides: ConfigMapWatcherService and DynamicPollScheduler services
  provides:
    - DI registration of ConfigMapWatcherService (K8s) and DynamicPollScheduler (both modes)
    - Clean Program.cs without legacy oidmap/devices file scanning
    - Unified simetra-config.json with all 92 OIDs for local development
    - Symmetric local dev startup (UpdateMap + ReloadAsync + ReconcileAsync)
  affects: [15-04, 15-05]
tech-stack:
  added: []
  patterns: [dual-mode-startup, generous-thread-pool-ceiling]
key-files:
  created:
    - src/SnmpCollector/config/simetra-config.json
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Program.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
    - tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
key-decisions:
  - "DynamicPollScheduler registered in both K8s and local dev modes for symmetric ReconcileAsync"
  - "Thread pool ceiling of 50 to accommodate dynamic device additions at runtime"
  - "OidMapOptions binding removed from DI -- OidMapService receives empty Dictionary at construction"
patterns-established:
  - "Dual-mode startup: K8s mode uses ConfigMapWatcherService, local dev uses file-based config loading after DI build"
  - "Generous thread pool ceiling (50) instead of exact count for dynamic job additions"
duration: 5min
completed: 2026-03-07
---

# Phase 15 Plan 03: DI Wiring and Local Dev Config Summary

**DI wiring for ConfigMapWatcherService in K8s mode, symmetric local dev config loading from unified simetra-config.json with 92 OIDs, and Program.cs cleanup removing legacy file scanning.**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-03-07T20:47:53Z
- **Completed:** 2026-03-07T20:55:00Z
- **Tasks:** 3
- **Files modified:** 5

## Accomplishments
- OidMapService registered with empty initial map (no IOptionsMonitor), populated at startup from config
- ConfigMapWatcherService registered as hosted service in K8s mode only; DynamicPollScheduler available in both modes
- Program.cs cleaned up: removed oidmap-*.json auto-scan and devices.json loading
- Unified simetra-config.json created with all 92 OIDs (24 OBP + 68 NPB) and 2 device entries for local dev
- Local dev mode calls OidMapService.UpdateMap, DeviceRegistry.ReloadAsync, and DynamicPollScheduler.ReconcileAsync after DI build

## Task Commits

Each task was committed atomically:

1. **Task 1: Update ServiceCollectionExtensions.cs for new DI wiring** - `e11397d` (feat)
2. **Task 2: Update Program.cs to remove legacy config scanning and add local dev reload** - `403936c` (feat)
3. **Task 3: Create unified simetra-config.json for local development** - `3e8097b` (feat)

## Files Created/Modified
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - DI registration for ConfigMapWatcherService, OidMapService with empty map, DynamicPollScheduler, thread pool ceiling
- `src/SnmpCollector/Program.cs` - Removed legacy file scanning, added local dev config loading with ReconcileAsync
- `src/SnmpCollector/config/simetra-config.json` - Unified config with all 92 OIDs and 2 devices for local dev
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - Updated OidMapService construction to use Dictionary
- `tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs` - Updated to read OBP entries from unified simetra-config.json

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| DynamicPollScheduler registered outside IsInCluster block | Local dev also calls ReconcileAsync for symmetric K8s/local behavior |
| Thread pool ceiling of 50 | Generous headroom for dynamic device additions without thread pool resize |
| OidMapOptions binding removed entirely | OidMapService now uses direct Dictionary constructor; IConfiguration binding unnecessary |
| app.Run() changed to await app.RunAsync() | Required for async ReloadAsync/ReconcileAsync calls before host starts |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed PipelineIntegrationTests for new OidMapService constructor**
- **Found during:** Task 3 verification
- **Issue:** PipelineIntegrationTests registered OidMapService via `AddSingleton<IOidMapService, OidMapService>()` which failed because the constructor now requires `Dictionary<string, string>` and `ILogger` -- DI cannot auto-resolve Dictionary.
- **Fix:** Changed to factory registration pattern matching ServiceCollectionExtensions: `AddSingleton<OidMapService>(sp => new OidMapService(dict, logger))` with forwarding interface registration.
- **Files modified:** tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
- **Verification:** All 6 PipelineIntegration tests pass
- **Committed in:** 3e8097b (Task 3 commit)

**2. [Rule 3 - Blocking] Fixed OidMapAutoScanTests for missing oidmap-obp.json**
- **Found during:** Task 3 verification
- **Issue:** 3 OidMapAutoScanTests referenced oidmap-obp.json which no longer exists on disk. Tests loaded the file via AddJsonFile and bound to OidMapOptions.
- **Fix:** Updated tests to parse simetra-config.json directly using SimetraConfigModel JSONC deserialization, then filter OBP entries by enterprise prefix for assertions.
- **Files modified:** tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
- **Verification:** All 5 OidMapAutoScan tests pass
- **Committed in:** 3e8097b (Task 3 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both fixes necessary to maintain passing test suite. No scope creep.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- DI wiring complete: ConfigMapWatcherService, DynamicPollScheduler, OidMapService all properly registered
- Program.cs startup clean: no legacy file scanning, dual-mode config loading works
- Plan 15-04 (K8s manifests) can update ConfigMap to use the new simetra-config.json key format
- Plan 15-05 (tests) can test DynamicPollScheduler reconciliation logic

---
*Phase: 15-k8s-configmap-watch-and-unified-config*
*Completed: 2026-03-07*
