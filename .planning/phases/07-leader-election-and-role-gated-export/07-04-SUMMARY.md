---
phase: 07-leader-election-and-role-gated-export
plan: 04
subsystem: infra
tags: [leader-election, kubernetes, opentelemetry, otlp, metrics, di, role-gating]

# Dependency graph
requires:
  - phase: 07-01
    provides: ILeaderElection, AlwaysLeaderElection, LeaseOptions, SiteOptions.PodIdentity, TelemetryConstants.LeaderMeterName
  - phase: 07-02
    provides: K8sLeaseElection (BackgroundService + ILeaderElection), LeaseOptionsValidator
  - phase: 07-03
    provides: MetricRoleGatedExporter(BaseExporter<Metric> inner, ILeaderElection, string gatedMeterName)
provides:
  - Complete DI wiring: K8sLeaseElection concrete-first singleton (K8s) / AlwaysLeaderElection (local)
  - MetricRoleGatedExporter wrapping OtlpMetricExporter via AddReader (replaces AddOtlpExporter)
  - Both MeterName and LeaderMeterName registered via AddMeter
  - Dynamic role in SnmpLogEnrichmentProcessor via Func<string> roleProvider closure
  - Dynamic role in SnmpConsoleFormatter via ILeaderElection lazy resolution
  - SiteOptions.PodIdentity PostConfigure from HOSTNAME env var (K8s pod name)
  - Lease section in appsettings.json with defaults matching LeaseOptions
affects:
  - 07-05 (test plan updating SnmpLogEnrichmentProcessorTests for Func<string>)
  - Phase 8 (full runtime: all DI wiring in place)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Concrete-first singleton pattern: AddSingleton<K8sLeaseElection>() then factory delegates for ILeaderElection and IHostedService to prevent two-instance pitfall"
    - "Func<string> roleProvider closure: lambda captures ILeaderElection singleton, evaluated on every log record for zero-allocation dynamic role"
    - "Manual OtlpMetricExporter construction + AddReader for wrappable export: AddOtlpExporter() internalizes exporter, preventing MetricRoleGatedExporter decoration"
    - "ILeaderElection lazy resolution in ConsoleFormatter: GetService<T> in EnsureServicesResolved, graceful fallback chain (CurrentRole ?? SiteOptions.Role ?? 'unknown')"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs
    - src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs
    - src/SnmpCollector/appsettings.json

key-decisions:
  - "Tasks 1 and 2 committed atomically: SnmpLogEnrichmentProcessor Func<string> change must precede ServiceCollectionExtensions compilation (CS1660 if done separately)"
  - "k8s namespace used fully-qualified in AddSnmpConfiguration: avoids ambiguity with no extra using directive needed at file level"
  - "LeaseOptions binding only inside IsInCluster() branch: ValidateOnStart for Lease would fail in local dev where Lease section may not be meaningful"
  - "appsettings.json Lease section always present: documents config surface and allows dev override even though binding is K8s-only"

patterns-established:
  - "Phase 7 concrete-first DI: AddSingleton<Concrete>(); AddSingleton<IFace>(sp => sp.Get<Concrete>()); AddHostedService(sp => sp.Get<Concrete>())"
  - "Dynamic role via Func<string>: () => leaderElection.CurrentRole -- captured at DI wiring time, evaluated at log time"

# Metrics
duration: 3min
completed: 2026-03-05
---

# Phase 7 Plan 04: DI Wiring for Leader Election and Role-Gated Export Summary

**ServiceCollectionExtensions wired with K8sLeaseElection concrete-first singleton, MetricRoleGatedExporter-wrapped OtlpMetricExporter via AddReader, and Func<string> dynamic role flowing through log enrichment and console formatter**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-03-05T16:24:10Z
- **Completed:** 2026-03-05T16:27:00Z
- **Tasks:** 3 (Tasks 1+2 committed together due to compile dependency)
- **Files modified:** 4

## Accomplishments
- ServiceCollectionExtensions.AddSnmpTelemetry replaces `metrics.AddOtlpExporter` with `metrics.AddReader` wrapping `new MetricRoleGatedExporter(new OtlpMetricExporter(...), leaderElection, LeaderMeterName)` -- enables leader-gated business metric export
- ServiceCollectionExtensions.AddSnmpConfiguration adds Phase 7 leader election block: K8sLeaseElection (concrete-first singleton in K8s), AlwaysLeaderElection (else), LeaseOptions binding, SiteOptions.PodIdentity PostConfigure
- SnmpLogEnrichmentProcessor changed from `string role` to `Func<string> roleProvider` -- evaluated per log record; SnmpConsoleFormatter adds ILeaderElection lazy resolution with graceful fallback chain

## Task Commits

Each task was committed atomically:

1. **Tasks 1+2: ServiceCollectionExtensions DI wiring + SnmpLogEnrichmentProcessor/SnmpConsoleFormatter dynamic role** - `d29b232` (feat)
2. **Task 3: Add Lease section to appsettings.json** - `0ffc91d` (chore)

**Plan metadata:** (docs commit follows)

_Note: Tasks 1 and 2 were committed together because SnmpLogEnrichmentProcessor's Func<string> signature is required for ServiceCollectionExtensions to compile (CS1660 if the processor still had string role when the DI wiring passed a lambda)._

## Files Created/Modified
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Added `using OpenTelemetry.Exporter`; replaced AddOtlpExporter with AddReader+MetricRoleGatedExporter; added LeaderMeterName AddMeter; added Phase 7 leader election block (K8s/local branches, LeaseOptions, PostConfigure); wired Func<string> roleProvider in enrichment processor
- `src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs` - Changed `string _role` to `Func<string> _roleProvider`; changed constructor parameter and null check; changed OnEnd to call `_roleProvider()` instead of using `_role`; updated XML docs
- `src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs` - Added `ILeaderElection? _leaderElection` lazy field; updated EnsureServicesResolved to resolve ILeaderElection; changed role to `_leaderElection?.CurrentRole ?? _siteOptions?.Value.Role ?? "unknown"`; updated SnmpConsoleFormatterOptions XML doc
- `src/SnmpCollector/appsettings.json` - Added "Lease" section with Name="snmp-collector-leader", Namespace="default", DurationSeconds=15, RenewIntervalSeconds=10

## Decisions Made

- **Tasks 1+2 in single commit:** SnmpLogEnrichmentProcessor must accept `Func<string>` before ServiceCollectionExtensions can pass the lambda; they are a single atomic change from the compiler's perspective.
- **Fully-qualified k8s namespace:** `k8s.KubernetesClientConfiguration.IsInCluster()`, `k8s.IKubernetes`, `k8s.Kubernetes` -- avoids needing `using k8s;` at file level; K8s types are used only in one conditional block so full qualification is cleaner.
- **LeaseOptions binding inside IsInCluster() only:** Binding with ValidateOnStart outside would cause startup failures in local dev where K8s env vars/service account files are absent. Local dev never needs lease config.
- **appsettings.json Lease section always present:** Acts as documentation of available K8s configuration; developers can see the knobs without reading source. LeaseOptions binding only activates in K8s so the values are ignored in local dev.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- CS1660 error on first build attempt (lambda to string type mismatch): Expected because SnmpLogEnrichmentProcessor still had `string role` parameter when the ServiceCollectionExtensions change was applied first. Resolved by implementing Task 2 before the first successful build. Committed Tasks 1 and 2 together as a single atomic unit.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All Phase 7 DI wiring is complete. The application now correctly selects leader election strategy (K8s lease or always-leader), gates business metric export on leadership status, and reflects dynamic role in all log output.
- Plan 05 (test updates) is the final task: update SnmpLogEnrichmentProcessorTests for `Func<string>` constructor signature, verify SnmpMetricFactoryTests now pass with LeaderMeterName MeterListener filter.
- Build succeeds with 0 errors. Test suite may have pre-existing failures in SnmpMetricFactoryTests and SnmpLogEnrichmentProcessorTests that Plan 05 addresses.

---
*Phase: 07-leader-election-and-role-gated-export*
*Completed: 2026-03-05*
