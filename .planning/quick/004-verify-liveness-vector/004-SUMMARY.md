# Quick Task 004: Verify Liveness Vector

**Status:** Complete (verification only, no code changes)
**Date:** 2026-03-05

## Claim 1: One entry per scheduled job (poll jobs, heartbeat job, correlation job)

**PARTIALLY CONFIRMED — no heartbeat job exists**

| Job Type | Stamps? | Evidence |
|----------|---------|----------|
| MetricPollJob | Yes | `MetricPollJob.cs:133` — `_liveness.Stamp(jobKey)` in finally block |
| CorrelationJob | Yes | `CorrelationJob.cs:52` — `_liveness.Stamp(jobKey)` in finally block |
| Heartbeat job | **DOES NOT EXIST** | `grep -i heartbeat` → 0 matches. Listed in v2 requirements as `OPS-02: Heartbeat loopback job proving full pipeline liveness` |

Registered intervals in `ServiceCollectionExtensions.cs`:
- `"correlation"` → `correlationOptions.IntervalSeconds` (line 404)
- `"metric-poll-{device.Name}-{pi}"` → `poll.IntervalSeconds` per device/poll group (line 430)

No other job types register intervals or stamp the liveness vector.

## Claim 2: Holds last completion timestamp per job

**CONFIRMED**

| Point | Evidence |
|-------|----------|
| Data type | `LivenessVectorService.cs:11` — `ConcurrentDictionary<string, DateTimeOffset> _stamps` |
| Stamp writes UTC now | `LivenessVectorService.cs:12` — `_stamps[jobKey] = DateTimeOffset.UtcNow` |
| Interface contract | `ILivenessVectorService.cs:15-19` — `void Stamp(string jobKey)` records "current UTC time" |
| Snapshot retrieval | `LivenessVectorService.cs:13` — `GetAllStamps()` returns defensive copy |

## Claim 3: Stamped at end of every job completion (not by traps, only scheduled jobs)

**CONFIRMED**

| Point | Evidence |
|-------|----------|
| MetricPollJob stamps in finally | `MetricPollJob.cs:128-133` — `finally { ... _liveness.Stamp(jobKey); }` |
| CorrelationJob stamps in finally | `CorrelationJob.cs:49-53` — `finally { _liveness.Stamp(jobKey); }` |
| Trap listener — NO stamp | `SnmpTrapListenerService.cs` — zero references to ILivenessVectorService |
| Channel consumer — NO stamp | `ChannelConsumerService.cs` — zero references to ILivenessVectorService |
| Interface doc explicit | `ILivenessVectorService.cs:8-9` — "Stamps are written ONLY by job completion -- not by trap arrival, not by pipeline processing" |

## Claim 4: Used by K8s liveness probe to detect flow hangs and job stalls

**CONFIRMED**

| Point | Evidence |
|-------|----------|
| LivenessHealthCheck reads stamps | `LivenessHealthCheck.cs:42` — `_liveness.GetAllStamps()` |
| Staleness formula | `LivenessHealthCheck.cs:51` — `threshold = intervalSeconds * graceMultiplier` |
| Unhealthy when stale | `LivenessHealthCheck.cs:54,72` — age > threshold → `HealthCheckResult.Unhealthy` with diagnostic data |
| Wired to `/healthz/live` | `Program.cs:54-56` — `MapHealthChecks("/healthz/live", ... tags.Contains("live"))` |
| Tagged "live" | `ServiceCollectionExtensions.cs:462` — `.AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "live" })` |
| K8s timing math | `LivenessHealthCheck.cs:16-17` — doc: "periodSeconds=15 and failureThreshold=3 means 45s of consecutive stale responses before pod restart" |

## Summary

| Claim | Verdict |
|-------|---------|
| One entry per scheduled job (poll + heartbeat + correlation) | **PARTIAL** — no heartbeat job exists (v2 requirement OPS-02) |
| Holds last completion timestamp per job | **CONFIRMED** — `ConcurrentDictionary<string, DateTimeOffset>` |
| Stamped at end of every job (not traps) | **CONFIRMED** — finally block in both jobs; zero trap/consumer references |
| Used by K8s liveness probe | **CONFIRMED** — LivenessHealthCheck on `/healthz/live` endpoint |
