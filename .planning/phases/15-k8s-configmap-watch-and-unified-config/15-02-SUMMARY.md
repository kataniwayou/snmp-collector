# Phase 15 Plan 02: ConfigMap Watcher and Dynamic Poll Scheduler Summary

**One-liner:** K8s ConfigMap watch service with JSONC parsing, reconnect loop, and Quartz job reconciliation engine for live config reload.

## What Was Done

### Task 1: DynamicPollScheduler for Quartz job reconciliation
Created `src/SnmpCollector/Services/DynamicPollScheduler.cs` with `ReconcileAsync` that diffs current metric-poll-* Quartz jobs against desired device config and adds new jobs, removes stale jobs, and reschedules changed intervals. Cleans up JobIntervalRegistry and LivenessVectorService entries on job removal.

### Task 2: ConfigMapWatcherService with K8s API watch and reconnect loop
Created `src/SnmpCollector/Services/ConfigMapWatcherService.cs` as a BackgroundService that:
- Reads initial config from the `simetra-config` ConfigMap on startup
- Watches via K8s API with automatic reconnect on ~30 min timeout
- Parses JSONC (comments + trailing commas) from the `simetra-config.json` key
- Serializes concurrent reloads via SemaphoreSlim
- Orchestrates OidMapService.UpdateMap, DeviceRegistry.ReloadAsync, DynamicPollScheduler.ReconcileAsync

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Reconnect pattern matches K8sLeaseElection | Consistent error handling and reconnect strategy across K8s watchers |
| 5-second delay on unexpected disconnect | Prevents tight reconnect loop on persistent API errors |
| ConfigMap deletion retains current config | Defensive: accidental deletion should not wipe running config |
| Namespace read from service account mount | Standard K8s in-cluster pattern; "simetra" fallback for local dev |

## Deviations from Plan

None -- plan executed exactly as written.

## Key Files

| File | Role |
|------|------|
| `src/SnmpCollector/Services/DynamicPollScheduler.cs` | Quartz job reconciliation engine |
| `src/SnmpCollector/Services/ConfigMapWatcherService.cs` | K8s ConfigMap watcher with reconnect loop |

## Commits

| Hash | Description |
|------|-------------|
| c0a30c0 | feat(15-02): add DynamicPollScheduler for Quartz job reconciliation |
| 7bd1aa9 | feat(15-02): add ConfigMapWatcherService with K8s watch and reconnect loop |

## Duration

~3 minutes

## Next Phase Readiness

Plan 15-03 (DI registration and integration) can proceed -- both services are ready for registration in ServiceCollectionExtensions.
