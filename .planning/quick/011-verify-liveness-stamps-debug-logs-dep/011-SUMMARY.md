# Quick Task 011: Verify Liveness Stamps, Debug Logs, Deploy

**One-liner:** Verified all IJob implementations stamp LivenessVectorService, set K8s log level to Debug, built simetra:local image and deployed 3-replica deployment to local K8s cluster.

**Duration:** ~10 min (including Docker Desktop startup gate)
**Completed:** 2026-03-06

## Task 1: Liveness Stamp Verification

### IJob Implementations Found

| Job | File | Stamp Call | Finally Block | ILivenessVectorService Injected |
|-----|------|-----------|---------------|-------------------------------|
| MetricPollJob | `src/SnmpCollector/Jobs/MetricPollJob.cs` | Line 140: `_liveness.Stamp(jobKey)` | Yes | Yes (constructor param line 31) |
| CorrelationJob | `src/SnmpCollector/Jobs/CorrelationJob.cs` | Line 57: `_liveness.Stamp(jobKey)` | Yes | Yes (constructor param line 17) |

### JobIntervalRegistry Alignment

Both jobs use `context.JobDetail.Key.Name` as the stamp key. The `AddSnmpScheduling` method in `ServiceCollectionExtensions.cs` registers identical keys:

| Registered Key | Registration Site | Stamp Consumer |
|---------------|-------------------|----------------|
| `"correlation"` | Line 401: `intervalRegistry.Register("correlation", ...)` | CorrelationJob via `context.JobDetail.Key.Name` |
| `"metric-poll-{device.Name}-{pi}"` | Line 427: `intervalRegistry.Register(...)` | MetricPollJob via `context.JobDetail.Key.Name` |

Job keys match between `JobKey` creation (lines 390, 411), `Register()` calls, and `Stamp()` usage.

### Verdict

All scheduled jobs correctly stamp the liveness vector. No missing stamps found. Both jobs:
- Inject `ILivenessVectorService` via constructor
- Call `_liveness.Stamp(jobKey)` in a `finally` block (executes on success AND failure)
- Use the Quartz `JobDetail.Key.Name` which matches the registered interval keys

## Task 2: K8s ConfigMap Log Level Change

**Commit:** `1d2d3a5` -- `chore(quick-011): set K8s log level to Debug for troubleshooting`

**Change:** Added `"LogLevel": { "Default": "Debug" }` to the `Logging` block in `deploy/k8s/configmap.yaml` (`appsettings.k8s.json` overlay).

Before:
```json
"Logging": {
  "EnableConsole": true
}
```

After:
```json
"Logging": {
  "LogLevel": {
    "Default": "Debug"
  },
  "EnableConsole": true
}
```

The base `appsettings.json` retains `"Default": "Information"`. The K8s overlay overrides it to Debug for cluster troubleshooting.

## Task 3: Docker Build and K8s Deploy

### Docker Build

- Image: `simetra:local` (tag matches deployment.yaml `imagePullPolicy: Never`)
- Dockerfile: `src/SnmpCollector/Dockerfile` (build context = repo root)
- Build: cached layers for restore, rebuilt publish stage (~4s)

### K8s Manifest Application

```
namespace/simetra           unchanged
serviceaccount/simetra-sa   unchanged
role/simetra-lease-role     unchanged
rolebinding/simetra-lease-binding unchanged
configmap/simetra-config    configured    <-- Debug log level applied
service/simetra             unchanged
deployment/simetra          created
```

### Deployment Verification

| Check | Result |
|-------|--------|
| Pod count | 3/3 Running |
| Startup probe | Passed (all 3 pods) |
| Liveness probe (`/healthz/live`) | 200 Healthy |
| Readiness probe (`/healthz/ready`) | 503 Unhealthy ("No device channels registered") |
| Debug logs visible | Yes -- `[DBG]` lines confirmed in pod logs |
| CorrelationJob running | Yes -- rotation logged at INF level |
| Leader election | Active -- pods report `follower` role |

**Readiness 503 is expected:** The configmap has `"Devices": []` (empty). The `ReadinessHealthCheck` requires `DeviceChannelManager.DeviceNames.Count > 0`. Pods are fully operational but report not-ready because there are no devices to poll. Adding devices to the configmap will resolve readiness.

### Log Sample Confirming Debug Level

```
2026-03-06T10:32:07Z [DBG] [...] Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService Health check processing with combined status Unhealthy completed after 0.1244ms
2026-03-06T10:32:08Z [DBG] [...] Quartz.Core.JobRunShell Calling Execute on job DEFAULT.correlation
2026-03-06T10:32:08Z [INF] [...] SnmpCollector.Jobs.CorrelationJob Correlation ID rotated to c797fd32f37144a7a0e236e9bb9a399d
```

## Authentication Gates

During execution, Docker Desktop was not running when Task 3 began:
- Paused for user to start Docker Desktop with Kubernetes enabled
- Resumed after Docker daemon and K8s cluster became reachable
- Build and deploy completed successfully

## Deviations from Plan

None -- plan executed exactly as written.

## Key Files

| File | Action |
|------|--------|
| `deploy/k8s/configmap.yaml` | Modified (added LogLevel.Default=Debug) |
| `src/SnmpCollector/Jobs/MetricPollJob.cs` | Verified (no changes) |
| `src/SnmpCollector/Jobs/CorrelationJob.cs` | Verified (no changes) |
| `src/SnmpCollector/Pipeline/LivenessVectorService.cs` | Verified (no changes) |
| `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` | Verified (no changes) |
