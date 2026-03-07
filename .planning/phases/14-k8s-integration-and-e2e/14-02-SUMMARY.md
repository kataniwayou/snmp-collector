# Phase 14 Plan 02: Device ConfigMap Summary

**One-liner:** devices.json ConfigMap key with 92 OIDs across OBP-01 (24) and NPB-01 (68), K8s DNS addresses, explicit community strings, 10s poll interval

## What Was Done

### Task 1: Add devices.json key to simetra-config ConfigMap
- Added `devices.json` key alongside existing `appsettings.k8s.json`, `oidmap-obp.json`, `oidmap-npb.json`
- OBP-01: 24 OIDs (4 links x 6 metrics), address `obp-simulator.simetra.svc.cluster.local`, community `Simetra.OBP-01`
- NPB-01: 68 OIDs (4 system + 8 ports x 8 metrics), address `npb-simulator.simetra.svc.cluster.local`, community `Simetra.NPB-01`
- Both use single MetricPoll with 10-second interval and plain string OID arrays (matches C# MetricPollOptions model)
- Removed empty `"Devices": []` from `appsettings.k8s.json` to prevent config binding conflict
- Cross-verified all OID strings match oidmap-obp.json and oidmap-npb.json exactly
- Confirmed deployment.yaml already has CONFIG_DIRECTORY=/app/config and directory mount (no subPath)
- Commit: `8e2ca38`

### Task 2: Remove obsolete configmap-devices.yaml template
- Deleted `deploy/k8s/simulators/configmap-devices.yaml` (used placeholder IPs and wrong MetricPoll structure)
- Verified no dangling references in deploy/ directory
- Commit: `78cb3ba`

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Single MetricPoll per device with all OIDs | Simpler config, all OIDs share same 10s interval |
| Removed Devices from appsettings.k8s.json | Avoids config binding conflict with dedicated devices.json |

## Deviations from Plan

None - plan executed exactly as written.

## Verification Results

- YAML syntax valid (python yaml.safe_load)
- OBP OIDs: 24/24 match oidmap-obp.json
- NPB OIDs: 68/68 match oidmap-npb.json
- Total: 92 OIDs
- K8s Service DNS addresses confirmed
- Explicit CommunityString on both entries
- deployment.yaml CONFIG_DIRECTORY=/app/config confirmed, no subPath
- Obsolete template removed, no dangling references

## Key Files

| File | Action | Description |
|------|--------|-------------|
| deploy/k8s/configmap.yaml | Modified | Added devices.json key, removed empty Devices from appsettings |
| deploy/k8s/simulators/configmap-devices.yaml | Deleted | Obsolete placeholder template |

## Metrics

- Duration: ~2 minutes
- Completed: 2026-03-07
- Tasks: 2/2
