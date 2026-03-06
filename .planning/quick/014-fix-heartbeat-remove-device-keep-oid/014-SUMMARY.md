# Quick Task 014: Fix heartbeat config — remove device entry, keep OidMap only

**One-liner:** Removed heartbeat device entry from Devices[] in all configs; kept OidMap entry and HeartbeatJob section since trap path validates community string only, not device registry.

## What Was Done

### Correction from Quick-013
The heartbeat is an internal trap sent by the HeartbeatJob to the local SNMP listener. The trap path only validates the community string via `CommunityStringHelper.TryExtractDeviceName` — it does NOT look up devices in the registry. Therefore:

- **Removed:** Heartbeat device entry from Devices[] (not needed — trap path doesn't use device registry)
- **Kept:** OidMap entry `1.3.6.1.4.1.9999.1.1.1.0 → simetraHeartbeat` (needed for metric name resolution in OidResolutionBehavior)
- **Kept:** HeartbeatJob config section with IntervalSeconds (needed for Quartz job scheduling)
- **Updated:** Production configmap documentation to explain OidMap entry instead of device entry

### Files Modified

| File | Change |
|------|--------|
| src/SnmpCollector/appsettings.Development.json | Removed heartbeat device from Devices[] |
| deploy/k8s/configmap.yaml | Removed heartbeat device from Devices[] |
| deploy/k8s/production/configmap.yaml | Removed heartbeat device, updated docs |

### Verification
- All JSON valid
- Build: 0 errors
- Tests: 115 passed, 0 failed

## Duration

~2 min
