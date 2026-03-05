---
phase: 03-mediatr-pipeline-and-instruments
plan: 06
subsystem: testing
tags: [xunit, mediatr, snmp, otel, pipeline, behaviors, integration-testing]

# Dependency graph
requires:
  - phase: 03-05
    provides: AddSnmpPipeline extension method, all Phase 3 services registered in DI
  - phase: 03-04
    provides: OtelMetricHandler, ISnmpMetricFactory, SnmpMetricFactory with truncation
  - phase: 03-01-03-03
    provides: SnmpOidReceived, all 4 pipeline behaviors, PipelineMetricService
provides:
  - Comprehensive unit tests for all 5 Phase 3 success criteria
  - TestSnmpMetricFactory in-memory stub for assertion in any test
  - Behavior unit tests (LoggingBehavior, ExceptionBehavior, ValidationBehavior, OidResolutionBehavior)
  - OtelMetricHandler tests (TypeCode dispatch, label correctness, counter deferral, info truncation)
  - PipelineIntegrationTests using ISender.Send with real DI container (end-to-end SC#1-5)
  - Bug fix: SnmpOidReceived changed from INotification to IRequest<Unit> so behaviors execute
affects:
  - 04-counter-delta-engine
  - 05-snmp-listener-and-trap-dispatch
  - 06-poll-executor
  - All future phases publishing SnmpOidReceived (must use ISender.Send not IPublisher.Publish)

# Tech tracking
tech-stack:
  added:
    - Microsoft.Extensions.DependencyInjection 9.0.0 (added explicitly to test project)
  patterns:
    - TestSnmpMetricFactory: in-memory ISnmpMetricFactory for behavioral assertion
    - Behavior isolation testing: construct behavior directly with NullLogger and stub dependencies
    - IMeterFactory via ServiceCollection.AddMetrics() for PipelineMetricService in tests
    - CapturingLoggerProvider: log message ordering proof for SC #5 behavior order

key-files:
  created:
    - tests/SnmpCollector.Tests/Helpers/TestSnmpMetricFactory.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/LoggingBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/ExceptionBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/ValidationBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
  modified:
    - src/SnmpCollector/Pipeline/SnmpOidReceived.cs (INotification -> IRequest<Unit>)
    - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs (INotificationHandler -> IRequestHandler; Task.CompletedTask -> Task.FromResult(Unit.Value))
    - src/SnmpCollector/Pipeline/Behaviors/LoggingBehavior.cs (constraint: INotification -> notnull)
    - src/SnmpCollector/Pipeline/Behaviors/ExceptionBehavior.cs (constraint: INotification -> notnull)
    - src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs (constraint: INotification -> notnull)
    - src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs (constraint: INotification -> notnull)
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs (remove TaskWhenAllPublisher; update doc)
    - tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj (added Microsoft.Extensions.DependencyInjection)

key-decisions:
  - "SnmpOidReceived changed from INotification to IRequest<Unit>: MediatR IPipelineBehavior ONLY fires for IRequest<T> dispatched via ISender.Send, not for INotification dispatched via IPublisher.Publish -- behaviors were silently dead code"
  - "OtelMetricHandler changed from INotificationHandler to IRequestHandler<SnmpOidReceived, Unit>: required for ISender.Send dispatch path"
  - "Behavior constraints changed from 'where T : INotification' to 'where T : notnull': required since SnmpOidReceived no longer implements INotification"
  - "TaskWhenAllPublisher removed from AddSnmpPipeline: not needed for IRequest<Unit> request/response pipeline"
  - "ISnmpMetricFactory override in integration test: register TestSnmpMetricFactory AFTER AddSnmpPipeline -- last-registered wins in DI"
  - "RequestHandlerDelegate<TResponse> in MediatR 12.5.0 takes CancellationToken parameter: use 'ct =>' not '() =>' in test lambdas"
  - "snmp_info truncation tested via CapturingSnmpMetricFactory wrapping real SnmpMetricFactory: truncation is in SnmpMetricFactory.RecordInfo, not OtelMetricHandler"
  - "SC #5 behavior order verified via CapturingLoggerProvider: LoggingBehavior category appears in log messages, confirming it ran before handler"

patterns-established:
  - "Test DI setup pattern: ServiceCollection.AddMetrics() + Options.Create(new SiteOptions{Name='test-site'}) + AddSingleton<PipelineMetricService>() for any test needing PipelineMetricService"
  - "Behavior isolation: construct behavior directly (new LoggingBehavior<T,U>(NullLogger.Instance)) without DI container for unit tests"
  - "TestSnmpMetricFactory as universal assertion helper: register last after AddSnmpPipeline to override SnmpMetricFactory via DI last-wins"
  - "StubDeviceRegistry and StubOidMapService: inline sealed private classes with minimal interface implementation for isolation"

# Metrics
duration: 14min
completed: 2026-03-05
---

# Phase 3 Plan 06: Pipeline Behavior Tests and Integration Tests Summary

**49 tests covering all 5 Phase 3 success criteria with bug fix: SnmpOidReceived changed from INotification to IRequest<Unit> enabling IPipelineBehavior chain execution via ISender.Send**

## Performance

- **Duration:** 14 min
- **Started:** 2026-03-05T01:51:59Z
- **Completed:** 2026-03-05T02:05:59Z
- **Tasks:** 2/2
- **Files modified:** 15

## Accomplishments

- All 49 tests pass (17 Phase 2 existing + 32 new Phase 3): behaviors, handler, and integration
- Critical bug fixed: SnmpOidReceived was INotification; IPipelineBehavior silently never ran; changed to IRequest<Unit> so the full behavior chain executes
- SC #1 verified: Integer32 notification via ISender.Send records to snmp_gauge with correct 5-label taxonomy (metric_name, oid, agent, source, value)
- SC #2 verified: Malformed OID and unknown device IP both rejected by ValidationBehavior with no exception propagation
- SC #3 verified: Downstream factory exception swallowed by ExceptionBehavior, pipeline continues
- SC #5 verified: LoggingBehavior log entry captured before handler activity via CapturingLoggerProvider
- Counter32/Counter64 deferral to Phase 4 confirmed by integration and handler tests

## Task Commits

1. **Task 1: TestSnmpMetricFactory helper and behavior unit tests** - `8766fb1` (feat)
2. **Task 2: OtelMetricHandler tests, integration tests, and IRequest<Unit> bug fix** - `21f459b` (fix)

## Files Created/Modified

- `tests/SnmpCollector.Tests/Helpers/TestSnmpMetricFactory.cs` - In-memory ISnmpMetricFactory recording GaugeRecords/InfoRecords for assertion
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/LoggingBehaviorTests.cs` - AlwaysCallsNext, LogsDebugForSnmpOidReceived, PassesThroughNonSnmpNotification
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/ExceptionBehaviorTests.cs` - SwallowsException, PassesThrough, ReturnsDefault
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/ValidationBehaviorTests.cs` - OID format rejection, device resolution, DeviceName enrichment
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs` - MetricName from map, Unknown sentinel, always calls next
- `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` - TypeCode dispatch, label correctness, counter deferral, truncation
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - End-to-end via ISender.Send with real DI container
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` - INotification -> IRequest<Unit>
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` - INotificationHandler -> IRequestHandler<SnmpOidReceived, Unit>
- `src/SnmpCollector/Pipeline/Behaviors/LoggingBehavior.cs` - constraint: INotification -> notnull
- `src/SnmpCollector/Pipeline/Behaviors/ExceptionBehavior.cs` - constraint: INotification -> notnull
- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` - constraint: INotification -> notnull
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` - constraint: INotification -> notnull
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - remove TaskWhenAllPublisher; updated pipeline doc
- `tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` - add Microsoft.Extensions.DependencyInjection 9.0.0

## Decisions Made

- **INotification -> IRequest<Unit>:** MediatR `IPipelineBehavior` runs ONLY for `IRequest<T>` sent via `ISender.Send`. `INotification` dispatched via `IPublisher.Publish` bypasses all registered behaviors entirely. This was a silent architectural bug where all 4 behaviors were dead code. Confirmed via reflection probe: `RequestHandlerDelegate<T>` invokes pipeline only for the request path.
- **Constraint change:** `where TNotification : INotification` changed to `where TNotification : notnull` so behaviors register as open generics for `SnmpOidReceived : IRequest<Unit>`.
- **TaskWhenAllPublisher removed:** Only relevant for `INotification` multi-handler dispatch. With `IRequest<Unit>`, MediatR routes to a single handler (OtelMetricHandler).
- **RequestHandlerDelegate<T> in MediatR 12.5.0 takes CancellationToken:** `delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default)` — test lambdas must use `ct => ...` not `() => ...`.
- **Integration test ISnmpMetricFactory override:** Last-registered singleton wins in .NET DI; registering `TestSnmpMetricFactory` after `AddSnmpPipeline` (which registers `SnmpMetricFactory`) correctly overrides it.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] SnmpOidReceived was INotification causing all behaviors to silently never execute**

- **Found during:** Task 2 (PipelineIntegrationTests - PublishMalformedOid_NoGaugeRecorded_NoException failed because ValidationBehavior never ran)
- **Issue:** `IPipelineBehavior<TRequest, TResponse>` in MediatR 12.x only fires when `ISender.Send(IRequest<T>)` is used. `IPublisher.Publish(INotification)` bypasses the pipeline. All 4 behaviors (Logging, Exception, Validation, OidResolution) were registered but never called. Verified by runtime reflection probe.
- **Fix:** Changed `SnmpOidReceived : INotification` to `SnmpOidReceived : IRequest<Unit>`. Changed `OtelMetricHandler : INotificationHandler<SnmpOidReceived>` to `IRequestHandler<SnmpOidReceived, Unit>`. Updated all behavior constraints from `INotification` to `notnull`. Removed `TaskWhenAllPublisher` (not applicable to request path). Integration tests now use `ISender.Send` instead of `IPublisher.Publish`.
- **Files modified:** SnmpOidReceived.cs, OtelMetricHandler.cs, all 4 behavior .cs files, ServiceCollectionExtensions.cs
- **Verification:** All 49 tests pass including SC#2 (validation rejects malformed OID) and SC#5 (logging behavior order confirmed via CapturingLoggerProvider)
- **Committed in:** `21f459b` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 Rule 1 bug)
**Impact on plan:** Essential correctness fix -- behaviors were completely non-functional without this change. No scope creep. Future phases (05-snmp-listener, 06-poll-executor) must use `ISender.Send` when dispatching `SnmpOidReceived`.

## Issues Encountered

- **MediatR 12.5.0 RequestHandlerDelegate signature:** The delegate takes `CancellationToken` as a parameter (with `= default`). Test lambdas must be `ct => Task.FromResult(Unit.Value)` not `() => Task.FromResult(Unit.Value)`. CS1593 compilation error exposed this.
- **ISnmpMetricFactory not overridden correctly:** Initially assumed `AddSnmpPipeline` registers `SnmpMetricFactory` as `ISnmpMetricFactory`. In .NET DI, last-registered wins for `GetRequiredService<T>`. Registering `TestSnmpMetricFactory` after `AddSnmpPipeline` correctly overrides it. Confirmed by passing `SendInteger32_GaugeRecorded_WithCorrectLabels` test.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 3 all 6 plans complete -- full pipeline tested with 49 passing tests
- All 5 Phase 3 success criteria verified by automated tests
- Phase 4 (Counter Delta Engine) can proceed: Counter32/Counter64 deferral test in place to confirm deferred behavior remains correct
- **Critical constraint for Phase 5/6:** When dispatching `SnmpOidReceived` from trap listener (Phase 5) or poll executor (Phase 6), use `ISender.Send(snmpOidReceived)` NOT `IPublisher.Publish(snmpOidReceived)` -- the latter bypasses the entire behavior pipeline

---
*Phase: 03-mediatr-pipeline-and-instruments*
*Completed: 2026-03-05*
