---
phase: "10-metrics"
plan: "03"
subsystem: "trap-pipeline"
tags: ["channel", "trap", "community-string", "bounded-channel"]
dependency_graph:
  requires: ["10-01"]
  provides: ["ITrapChannel", "TrapChannel", "rewritten-trap-listener", "simplified-channel-consumer"]
  affects: ["10-04"]
tech_stack:
  added: []
  patterns: ["single-shared-channel", "community-string-convention-auth"]
file_tracking:
  key_files:
    created:
      - "src/SnmpCollector/Pipeline/ITrapChannel.cs"
      - "src/SnmpCollector/Pipeline/TrapChannel.cs"
    modified:
      - "src/SnmpCollector/Services/SnmpTrapListenerService.cs"
      - "src/SnmpCollector/Services/ChannelConsumerService.cs"
    deleted:
      - "src/SnmpCollector/Pipeline/IDeviceChannelManager.cs"
      - "src/SnmpCollector/Pipeline/DeviceChannelManager.cs"
decisions: []
metrics:
  duration: "~3 min"
  completed: "2026-03-06"
---

# Phase 10 Plan 03: Trap Path Rewrite Summary

Single shared BoundedChannel with community string convention auth replacing per-device channels and device registry dependency in trap path.

## What Was Done

### Task 1: Create ITrapChannel/TrapChannel, delete old channel manager
- Created `ITrapChannel` interface with Writer, Reader, Complete(), WaitForDrainAsync()
- Created `TrapChannel` implementation using BoundedChannel with DropOldest backpressure
- Drop callback increments snmp.trap.dropped via PipelineMetricService
- Deleted `IDeviceChannelManager` and `DeviceChannelManager` (per-device channel infrastructure removed)

### Task 2: Rewrite SnmpTrapListenerService and simplify ChannelConsumerService
- **SnmpTrapListenerService**: Complete rewrite
  - Removed IDeviceRegistry dependency (no device lookup for traps)
  - Removed IDeviceChannelManager dependency (replaced with ITrapChannel)
  - Removed _seenDevices ConcurrentDictionary (no per-device first-contact tracking)
  - Added CommunityStringHelper.TryExtractDeviceName validation
  - Invalid community string logged at Debug level (locked decision)
  - Added volatile _isBound flag and IsBound property for ReadinessHealthCheck (Plan 04)
  - All varbinds written to single shared ITrapChannel.Writer
- **ChannelConsumerService**: Simplified to single consumer
  - Replaced IDeviceChannelManager with ITrapChannel
  - Single await foreach loop on ITrapChannel.Reader.ReadAllAsync
  - Removed ConsumeDeviceAsync per-device method
  - Removed Task.WhenAll per-device pattern

## Verification Results

- SnmpTrapListenerService has zero references to IDeviceRegistry
- CommunityStringHelper.TryExtractDeviceName is called for community validation
- Invalid community string logged at Debug level (not Warning)
- ChannelConsumerService uses ITrapChannel.Reader with single consumer loop
- IDeviceChannelManager.cs and DeviceChannelManager.cs files deleted

## Deviations from Plan

None -- plan executed exactly as written.

## Commits

| Hash | Message |
|------|---------|
| b2c2bd4 | feat(10-03): create ITrapChannel/TrapChannel, delete old per-device channel manager |
| 8279885 | feat(10-03): rewrite trap listener and simplify channel consumer |

## Next Phase Readiness

Plan 04 must:
- Update ServiceCollectionExtensions to register ITrapChannel/TrapChannel (replacing IDeviceChannelManager)
- Update GracefulShutdownService to use ITrapChannel instead of IDeviceChannelManager
- Update ReadinessHealthCheck to use SnmpTrapListenerService.IsBound instead of DeviceNames.Count
- Update tests for new channel and listener contracts
