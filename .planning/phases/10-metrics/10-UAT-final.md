---
status: complete
phase: 10-metrics
source: 10-01-SUMMARY.md, 10-02-SUMMARY.md, 10-03-SUMMARY.md, 10-04-SUMMARY.md, 10-05-SUMMARY.md, 10-06-SUMMARY.md, 10-07-SUMMARY.md
started: 2026-03-06T19:00:00Z
updated: 2026-03-06T19:45:00Z
---

## Current Test

[testing complete]

## Tests

### 1. PHYSICAL_HOSTNAME env var for host_name label
expected: All host_name resolution uses PHYSICAL_HOSTNAME env var with MachineName fallback. HOSTNAME only for PodIdentity. K8s YAMLs inject PHYSICAL_HOSTNAME from spec.nodeName.
result: pass

### 2. Business metric labels (snmp_gauge and snmp_info)
expected: snmp_gauge has 7 labels: host_name, metric_name, oid, device_name, ip, source, snmp_type. snmp_info has same 7 + value (8 total). No site_name or agent labels anywhere.
result: pass

### 3. Pipeline counter labels (11 counters)
expected: All 11 pipeline counters tagged with host_name. snmp.trap.dropped also has device_name.
result: pass

### 4. Community string convention (Simetra.{DeviceName})
expected: Traps validated via CommunityStringHelper.TryExtractDeviceName. Invalid community dropped at Debug level. Polls use per-device configured CommunityString (validated as Simetra.* at startup).
result: pass

### 5. Per-device Port and CommunityString config
expected: DeviceOptions has Port (default 161) and CommunityString (required). DevicesOptionsValidator enforces Port 1-65535 and CommunityString starts with "Simetra.". MetricPollJob uses device.Port and device.CommunityString directly.
result: issue
reported: "CommunityString config property is redundant — Name already determines it via Simetra.{Name} convention. Remove CommunityString, derive from Name using CommunityStringHelper.DeriveFromDeviceName."
severity: minor

### 6. Single shared trap channel
expected: ITrapChannel/TrapChannel replaces per-device IDeviceChannelManager. One BoundedChannel for all traps. DropOldest backpressure. No device registry dependency in trap path.
result: pass

### 7. sysUpTime not auto-prepended
expected: MetricPollJob sends only configured OIDs from pollGroup.Oids. No SysUpTimeOid constant. Users who want sysUpTime add it to their OID list in appsettings.
result: pass

### 8. Trap and poll flow paths
expected: Poll and trap paths converge at MediatR pipeline. Both use ISender.Send with SnmpOidReceived.
result: pass

### 9. All tests pass
expected: dotnet test produces 115 passed, 0 failed, 0 skipped. Build has 0 errors, 0 warnings.
result: pass

## Summary

total: 9
passed: 8
issues: 1
pending: 0
skipped: 0

## Gaps

- truth: "Per-device config should not require redundant CommunityString when Name already determines it"
  status: failed
  reason: "User reported: CommunityString is redundant — derive from Name via CommunityStringHelper.DeriveFromDeviceName"
  severity: minor
  test: 5
  root_cause: "DeviceOptions has both Name and CommunityString but CommunityString is always Simetra.{Name}"
  artifacts:
    - path: "src/SnmpCollector/Configuration/DeviceOptions.cs"
      issue: "CommunityString property should be removed"
    - path: "src/SnmpCollector/Pipeline/DeviceInfo.cs"
      issue: "CommunityString parameter should be removed"
    - path: "src/SnmpCollector/Jobs/MetricPollJob.cs"
      issue: "Should derive community string from device.Name"
    - path: "src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs"
      issue: "CommunityString validation should be removed"
  missing:
    - "Remove CommunityString from DeviceOptions, DeviceInfo, DeviceRegistry"
    - "MetricPollJob derives community via CommunityStringHelper.DeriveFromDeviceName(device.Name)"
    - "Remove CommunityString validation from DevicesOptionsValidator"
    - "Update appsettings, configmap, and tests"
  debug_session: ""
