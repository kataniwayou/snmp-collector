---
phase: 06-poll-scheduling
plan: 04
subsystem: testing
tags: [xunit, snmp, quartz, mediatr, otel, metrics, unit-tests, ISnmpClient, IJobExecutionContext]

# Dependency graph
requires:
  - phase: 06-03
    provides: MetricPollJob registered in Quartz with DeviceUnreachabilityTracker singleton
  - phase: 06-02
    provides: MetricPollJob implementation with DispatchResponseAsync and failure tracking
  - phase: 06-01
    provides: DeviceUnreachabilityTracker implementation
provides:
  - ISnmpClient interface wrapping static Messenger.GetAsync for testability
  - SharpSnmpClient production implementation registered as singleton in AddSnmpPipeline
  - 8 DeviceUnreachabilityTracker unit tests covering all state transition paths
  - 8 MetricPollJob unit tests covering dispatch, sysUpTime propagation, noSuchObject skip, failures, recovery
  - Bug fix: sysUpTime extraction now happens after dispatch so sysUpTime's own SnmpOidReceived carries SysUpTimeCentiseconds=null
affects:
  - Phase 7 (leader election) — ISnmpClient pattern available for further testability
  - Future SNMP client extensions (v3, bulk GET) can implement ISnmpClient

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ISnmpClient interface pattern: wrap static SharpSnmpLib Messenger.GetAsync for unit test injection"
    - "StubJobExecutionContext pattern: implement IJobExecutionContext with only the members MetricPollJob reads"
    - "CapturingSender pattern: ISender stub capturing Send<TResponse> calls to List<SnmpOidReceived>"
    - "StubSnmpClient: ISnmpClient stub with configurable Response and ExceptionToThrow"
    - "EmptyAsyncEnumerable<T>: inline helper avoiding Linq.Async dependency for IAsyncEnumerable stubs"

key-files:
  created:
    - src/SnmpCollector/Pipeline/ISnmpClient.cs
    - src/SnmpCollector/Pipeline/SharpSnmpClient.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceUnreachabilityTrackerTests.cs
    - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
  modified:
    - src/SnmpCollector/Jobs/MetricPollJob.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "ISnmpClient wraps static Messenger.GetAsync — interface injection pattern rather than subclass/virtual override"
  - "SharpSnmpClient registered as singleton in AddSnmpPipeline (alongside other pipeline singletons)"
  - "sysUpTime extraction moved to AFTER dispatch: sysUpTime's own SnmpOidReceived carries SysUpTimeCentiseconds=null per stated semantic intent"
  - "StubJobExecutionContext implements full IJobExecutionContext with only needed members returning real values, rest throw NotImplementedException or return safe defaults"
  - "CapturingSender uses explicit interface implementation to avoid constraint mismatch — ISender.Send<TRequest> where TRequest : IBaseRequest"
  - "EmptyAsyncEnumerable<T> inline helper avoids adding System.Linq.Async package dependency"
  - "DeviceUnreachabilityTrackerTests use direct instantiation (new DeviceUnreachabilityTracker()) — no DI needed for pure in-memory class"

patterns-established:
  - "Stub SNMP client: configure Response list or ExceptionToThrow for deterministic test control"
  - "Capturing sender: use List<SnmpOidReceived> Sent property for varbind dispatch assertions"
  - "MeterListener tests in [Collection(NonParallelMeterTests)] to prevent cross-test measurement contamination"

# Metrics
duration: 7min
completed: 2026-03-05
---

# Phase 6 Plan 4: Unit Tests for DeviceUnreachabilityTracker and MetricPollJob Summary

**ISnmpClient abstraction enables full MetricPollJob unit testing: 16 new tests covering all state transitions, dispatch logic, sysUpTime propagation, and PollExecuted counter behavior (102 total passing)**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-05T14:57:37Z
- **Completed:** 2026-03-05T15:04:53Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments
- Created ISnmpClient interface and SharpSnmpClient implementation so MetricPollJob no longer depends on a static method
- 8 DeviceUnreachabilityTracker tests covering all state transitions (threshold, no-duplicate, recovery, re-unreachable, independent devices, case-insensitive)
- 8 MetricPollJob tests covering device-not-found, dispatch, sysUpTime propagation, noSuchObject skip, timeout/failure, 3x unreachable transition, recovery, and PollExecuted counter
- Fixed sysUpTime extraction ordering bug: extract AFTER dispatch so sysUpTime varbind's own SnmpOidReceived carries SysUpTimeCentiseconds=null per documented intent

## Task Commits

Each task was committed atomically:

1. **Task 1: DeviceUnreachabilityTracker unit tests** - `9462d70` (test)
2. **Task 2: MetricPollJob unit tests (+ ISnmpClient abstraction + bug fix)** - `008b8b9` (feat)

**Plan metadata:** _(docs commit follows)_

## Files Created/Modified
- `src/SnmpCollector/Pipeline/ISnmpClient.cs` - Interface wrapping static Messenger.GetAsync for testability
- `src/SnmpCollector/Pipeline/SharpSnmpClient.cs` - Production ISnmpClient delegating to Messenger.GetAsync, registered as singleton
- `src/SnmpCollector/Jobs/MetricPollJob.cs` - Inject ISnmpClient; fix sysUpTime extraction ordering (extract after dispatch)
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Register SharpSnmpClient as ISnmpClient singleton in AddSnmpPipeline
- `tests/SnmpCollector.Tests/Pipeline/DeviceUnreachabilityTrackerTests.cs` - 8 state transition tests via direct instantiation
- `tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs` - 8 MetricPollJob tests using StubSnmpClient + CapturingSender

## Decisions Made
- ISnmpClient wraps static Messenger.GetAsync via interface injection — cleaner than virtual method override approach
- SharpSnmpClient registered as singleton in AddSnmpPipeline alongside other pipeline singletons (ISnmpMetricFactory, ICounterDeltaEngine)
- sysUpTime extraction moved AFTER dispatch: the existing code extracted sysUpTime before dispatching in the same loop iteration, meaning the sysUpTime varbind's own message had SysUpTimeCentiseconds=500 instead of null — contradicting the documented intent ("sysUpTime own varbind has SysUpTimeCentiseconds = null")
- StubJobExecutionContext implements IJobExecutionContext with only JobDetail, MergedJobDataMap, CancellationToken, and Result — all other properties throw NotImplementedException or return safe zero/null defaults
- CapturingSender uses explicit interface implementation to match ISender constraint: `ISender.Send<TRequest>(TRequest, CancellationToken)` requires `where TRequest : IBaseRequest`
- EmptyAsyncEnumerable<T> inline helper avoids adding System.Linq.Async dependency; needed by ISender.CreateStream overloads

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed sysUpTime extraction ordering in MetricPollJob.DispatchResponseAsync**
- **Found during:** Task 2 (MetricPollJobTests — test Execute_SuccessfulPoll_SetsSystemUpTimeOnSubsequentVarbinds failed)
- **Issue:** sysUpTime extraction happened BEFORE dispatch in the same loop iteration. The sysUpTime varbind's own SnmpOidReceived was dispatched with SysUpTimeCentiseconds=500 (already extracted) instead of null. This contradicts the documented design decision in STATE.md: "sysUpTime own varbind has SysUpTimeCentiseconds = null; subsequent OIDs get extracted value"
- **Fix:** Moved sysUpTime extraction to AFTER the `await _sender.Send(msg, ct)` call but before the next loop iteration. sysUpTime varbind is now dispatched with null; subsequent OIDs carry the extracted value.
- **Files modified:** src/SnmpCollector/Jobs/MetricPollJob.cs
- **Verification:** Execute_SuccessfulPoll_SetsSystemUpTimeOnSubsequentVarbinds passes; all 102 tests pass
- **Committed in:** 008b8b9 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug)
**Impact on plan:** Bug fix restores intended behavior. Counter delta engine downstream depends on null SysUpTimeCentiseconds for the sysUpTime varbind itself to avoid treating it as a counter timestamp reference.

## Issues Encountered
- ISender interface in MediatR 12.5.0 has more overloads than expected: `Send(object, CancellationToken)`, `Send<TRequest>(TRequest, CancellationToken) where TRequest : IBaseRequest`, and two `CreateStream` overloads. CapturingSender required explicit interface implementation to avoid constraint mismatch.
- IJobExecutionContext has `JobInstance` (IJob), `Put(object, object)`, `Get(object)` members not listed in the plan's stub outline — added them with NotImplementedException / no-op implementations.
- `AsyncEnumerable.Empty<T>()` requires System.Linq.Async package — implemented inline EmptyAsyncEnumerable<T> instead.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 6 fully complete: DeviceUnreachabilityTracker, MetricPollJob, Quartz scheduling, and unit tests all in place
- 102 tests passing (previously 86)
- ISnmpClient pattern is available for Phase 7 if additional testability needed
- All Phase 6 success criteria met with automated test coverage

---
*Phase: 06-poll-scheduling*
*Completed: 2026-03-05*
