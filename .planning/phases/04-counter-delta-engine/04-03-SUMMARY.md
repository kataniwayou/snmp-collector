---
phase: 04-counter-delta-engine
plan: 03
subsystem: pipeline
tags: [snmp, mediatr, otel, counter, delta, di, integration-test]

# Dependency graph
requires:
  - phase: 04-01
    provides: SnmpOidReceived.SysUpTimeCentiseconds field and ISnmpMetricFactory.RecordCounter method
  - phase: 04-02
    provides: ICounterDeltaEngine interface and CounterDeltaEngine singleton implementation

provides:
  - OtelMetricHandler wired to ICounterDeltaEngine for Counter32 and Counter64 dispatch
  - ICounterDeltaEngine registered as singleton in AddSnmpPipeline DI extension
  - Integration test proving Counter32 delta=500 through the full MediatR pipeline
  - Handler unit tests updated to use real CounterDeltaEngine (first-poll baseline, no delta)

affects:
  - 04-04
  - 05-snmp-trap-receiver
  - 06-poll-scheduler

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "IncrementHandled called only when RecordDelta returns true (delta emitted, not first-poll baseline)"
    - "CounterDeltaEngine injected into OtelMetricHandler via constructor; registered singleton in AddSnmpPipeline"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs

key-decisions:
  - "ICounterDeltaEngine registered after ISnmpMetricFactory in AddSnmpPipeline; CounterDeltaEngine resolved with last-registered ISnmpMetricFactory (testFactory in tests)"
  - "IncrementHandled() called only on RecordDelta return true -- first-poll baseline does not count as handled"

patterns-established:
  - "Counter arms use block-scoped cases (braces) to hold local var declarations without fallthrough"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 4 Plan 03: Counter Delta Engine Wiring Summary

**Counter32 and Counter64 SNMP values now flow through CounterDeltaEngine end-to-end: OtelMetricHandler dispatches to delta engine via constructor injection, ICounterDeltaEngine is registered as singleton in AddSnmpPipeline, and integration tests prove a delta of 500 is recorded on second poll.**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-05T03:15:12Z
- **Completed:** 2026-03-05T03:17:32Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- OtelMetricHandler Counter32/Counter64 arms replaced: deferred log-and-skip removed, real delta engine calls added
- ICounterDeltaEngine registered as singleton in AddSnmpPipeline; no manual DI wiring needed in Program.cs
- Integration test SendCounter32_SecondPoll_CounterDeltaRecorded proves delta=500 through full MediatR pipeline
- OtelMetricHandlerTests updated with real CounterDeltaEngine; first-poll tests assert CounterRecords empty
- All 64 tests pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Wire CounterDeltaEngine into OtelMetricHandler and register in DI** - `500889b` (feat)
2. **Task 2: Update integration tests and handler tests for counter delta recording** - `3593b92` (test)

**Plan metadata:** (see final commit below)

## Files Created/Modified
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` - Added ICounterDeltaEngine field/constructor param; Counter32/Counter64 arms now call _deltaEngine.RecordDelta; IncrementHandled only on true return
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Added AddSingleton<ICounterDeltaEngine, CounterDeltaEngine> in AddSnmpPipeline
- `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` - Inject real CounterDeltaEngine; rename Counter32/Counter64 deferral tests to FirstPoll_NoCounterRecorded; assert CounterRecords empty
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - Rename NothingRecorded to FirstPoll_NoCounterRecorded; add SendCounter32_SecondPoll_CounterDeltaRecorded asserting delta=500

## Decisions Made
- ICounterDeltaEngine is registered after ISnmpMetricFactory in AddSnmpPipeline. In integration tests, the last-registered ISnmpMetricFactory (TestSnmpMetricFactory) is the one that CounterDeltaEngine receives at resolution time -- DI last-registration-wins semantics ensure the test factory captures counter records correctly.
- IncrementHandled() is called only when RecordDelta returns true. First-poll baseline storage is not considered a "handled" event from the pipeline metrics perspective.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Counter32 and Counter64 values now produce snmp_counter metrics end-to-end through the full pipeline
- Phase 4 Plan 04 (unit tests for all 5 CounterDeltaEngine delta paths) can proceed
- The blocker noted in STATE.md ("CounterDeltaEngine unit tests (all 5 paths) still needed") is addressed by 04-04

---
*Phase: 04-counter-delta-engine*
*Completed: 2026-03-05*
