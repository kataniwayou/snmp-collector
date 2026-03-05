# Quick Task 008: Verify Logs Business Logic & Explain Role Standalone

**Status:** Complete (verification + explanation)
**Date:** 2026-03-05

## 1. Console Logs — Business Logic Comparison

### Log Categories Emitted (all 3 pods identical)

| Category | Message Pattern | Frequency |
|----------|----------------|-----------|
| `SnmpCollector.Jobs.CorrelationJob` | "Correlation ID rotated to {id}" | Every ~30s |
| `Microsoft.AspNetCore.Hosting.Diagnostics` | "Request starting HTTP/1.1 GET …" / "Request finished … 200" | Readiness ~10s, Liveness ~15s |
| `Microsoft.AspNetCore.Routing.EndpointMiddleware` | "Executing endpoint 'Health checks'" / "Executed endpoint 'Health checks'" | Same as above |

### Business Logic Verified Identical Across Pods

| Behavior | Pod 1 (59srm) | Pod 2 (9s48h) | Pod 3 (t96t5) |
|----------|---------------|---------------|---------------|
| CorrelationJob fires | Every 30s | Every 30s | Every 30s |
| Readiness probes served | /healthz/ready → 200 | Same | Same |
| Liveness probes served | /healthz/live → 200 | Same | Same |
| Response times | 0.15–0.54ms | 0.14–0.76ms | 0.17–1.07ms |
| Log format | `{ts} [INF] [site\|role\|id] {cat} {msg}` | Identical | Identical |
| CorrelationId format | 32-char hex GUID | Identical | Identical |

### What DIFFERS Between Pods (expected)

| Attribute | Pod 1 (59srm) | Pod 2 (9s48h) | Pod 3 (t96t5) |
|-----------|---------------|---------------|---------------|
| **Role** | `leader` | `follower` | `follower` |
| **IP** | 10.1.2.130 | 10.1.2.131 | 10.1.2.132 |
| **CorrelationId values** | Independent rotation | Independent rotation | Independent rotation |
| **Rotation timing** | :46 offset | :52 offset | :44 offset |

### Conclusion

All 3 pods execute identical business logic:
- Same Quartz jobs (CorrelationJob at 30s interval)
- Same health probe endpoints (/healthz/ready, /healthz/live)
- Same ASP.NET middleware pipeline
- Same log format and categories

The only differences are role (determined by K8s Lease election), pod IP, and correlation ID values (each pod generates independently). No pipeline-level business metrics appear yet — expected since no SNMP device traffic exists in this test environment.

## 2. Explanation: What "Role": "standalone" in appsettings.json Is For

### The Config Entry

```json
{
  "Site": {
    "Name": "site-nyc-01",
    "Role": "standalone"
  }
}
```

Bound to `SiteOptions.Role` (default: `"standalone"`), defined in `Configuration/SiteOptions.cs:23`.

### Purpose: Phase 1 Static Role (Now Superseded)

`SiteOptions.Role` was introduced in **Phase 1** as the **only** role source before leader election existed. The log enrichment processor originally took `string role` (not `Func<string>`), and console logs would show `standalone` for all instances.

**Phase 7** introduced `ILeaderElection` with two implementations:
- **`K8sLeaseElection`** (in K8s): Returns `"leader"` or `"follower"` dynamically
- **`AlwaysLeaderElection`** (local dev): Always returns `"leader"`

Phase 7 changed `SnmpLogEnrichmentProcessor` to take `Func<string> roleProvider` bound to `() => leaderElection.CurrentRole`, making the static `SiteOptions.Role` obsolete for OTLP log enrichment.

### Current Consumers

| Consumer | How Role is resolved | Uses SiteOptions.Role? |
|----------|---------------------|----------------------|
| `SnmpLogEnrichmentProcessor` | `_roleProvider()` → `ILeaderElection.CurrentRole` | **No** — always dynamic |
| `SnmpConsoleFormatter` | `_leaderElection?.CurrentRole ?? _siteOptions?.Value.Role ?? "unknown"` | **Fallback only** — if `ILeaderElection` hasn't been resolved yet during early startup |

### When SiteOptions.Role Would Actually Be Used

1. **Never in K8s** — `K8sLeaseElection` is always available, returns "leader"/"follower"
2. **Never in local dev** — `AlwaysLeaderElection` always returns "leader"
3. **Briefly during early startup** — The console formatter lazily resolves DI services on first `Write()` call. Before resolution, `_leaderElection` is null, so the fallback chain hits `_siteOptions.Value.Role`. This window is typically <1 second.

### Summary

`"Role": "standalone"` is a **Phase 1 artifact** that served as the telemetry role tag before leader election was implemented. It is now effectively dead config — superseded by `ILeaderElection.CurrentRole` in all active code paths. Its only remaining function is as a null-guard fallback in the console formatter during the sub-second DI resolution window at startup.

The value `"standalone"` (vs `"leader"` or `"follower"`) was chosen to distinguish the non-K8s deployment mode where no election happens, but in practice:
- In K8s: overridden by "leader"/"follower" from lease election
- In local dev: overridden by "leader" from `AlwaysLeaderElection`
- The string `"standalone"` never appears in any live pod logs
