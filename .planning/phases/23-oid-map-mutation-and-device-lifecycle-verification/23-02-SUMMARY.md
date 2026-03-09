---
phase: 23
plan: 02
subsystem: e2e-testing
tags: [e2e, device-lifecycle, configmap, hot-reload, prometheus]
depends_on:
  requires: [23-01]
  provides: [device-lifecycle-e2e-fixtures, device-lifecycle-e2e-scenarios]
  affects: [24]
tech-stack:
  added: []
  patterns: [configmap-mutation-testing, counter-stagnation-detection, interval-comparison]
key-files:
  created:
    - tests/e2e/fixtures/device-added-configmap.yaml
    - tests/e2e/fixtures/device-removed-configmap.yaml
    - tests/e2e/fixtures/device-modified-interval-configmap.yaml
    - tests/e2e/scenarios/21-device-add.sh
    - tests/e2e/scenarios/22-device-remove.sh
    - tests/e2e/scenarios/23-device-modify-interval.sh
  modified: []
decisions:
  - id: DEV-COMMUNITY
    description: "E2E-SIM-2 uses explicit CommunityString override to Simetra.E2E-SIM since default convention would derive Simetra.E2E-SIM-2 which the simulator rejects"
  - id: DEV-REMOVE-STAGNATION
    description: "Device removal verified via counter stagnation (delta=0 over 20s) rather than counter absence due to Prometheus 5-min staleness"
  - id: DEV-INTERVAL-COMPARISON
    description: "Interval modification verified by comparing poll deltas across two 30s measurement windows at different intervals"
metrics:
  duration: ~3min
  completed: 2026-03-09
---

# Phase 23 Plan 02: Device Lifecycle E2E Fixtures and Scenarios Summary

**One-liner:** ConfigMap fixtures and shell scenarios for device add/remove/modify lifecycle verification via Prometheus counter assertions.

## What Was Done

### Task 1: Device Lifecycle Fixture Files

Created 3 complete simetra-devices ConfigMap YAML files, each a full copy of the baseline `fake-device-configmap.yaml` with one targeted modification:

1. **device-added-configmap.yaml** -- Adds E2E-SIM-2 device entry with explicit `CommunityString: "Simetra.E2E-SIM"` override (same simulator pod, distinct device identity, single OID .999.1.1.0)
2. **device-removed-configmap.yaml** -- Removes E2E-SIM entry entirely while retaining OBP-01, NPB-01, FAKE-UNREACHABLE
3. **device-modified-interval-configmap.yaml** -- Changes E2E-SIM IntervalSeconds from 10 to 5

### Task 2: Device Lifecycle Scenario Scripts (21-23)

Created 3 sourced-script scenarios following the established pattern (no shebang, no set -euo, all lib functions pre-sourced by run-all.sh):

1. **21-device-add.sh** -- Applies device-added ConfigMap, waits for DeviceWatcherService detection, polls until `snmp_poll_executed_total{device_name="E2E-SIM-2"}` increments, asserts delta > 0, restores
2. **22-device-remove.sh** -- Establishes baseline, applies device-removed ConfigMap, waits for flush, measures counter stagnation (delta = 0 over 20s window), restores and verifies polling resumes
3. **23-device-modify-interval.sh** -- Measures baseline poll delta at 10s interval over 30s, applies modified ConfigMap (5s interval), measures again over 30s, asserts faster interval produces more polls

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| DEV-COMMUNITY | E2E-SIM-2 uses explicit CommunityString override | Default convention derives Simetra.E2E-SIM-2 from device name, but simulator only accepts Simetra.E2E-SIM |
| DEV-REMOVE-STAGNATION | Removal verified via counter stagnation not absence | Prometheus 5-min staleness means counters persist after device stops polling |
| DEV-INTERVAL-COMPARISON | Interval change verified by comparing two measurement windows | Statistical approach: more polls in same time window at faster interval |

## Requirements Coverage

| Requirement | Scenario | Verification Method |
|-------------|----------|-------------------|
| DEV-01: Device addition | 21-device-add.sh | poll_executed counter increments for new device |
| DEV-02: Device removal | 22-device-remove.sh | poll_executed delta = 0 over stagnation window |
| DEV-03: Interval modification | 23-device-modify-interval.sh | poll delta comparison across measurement windows |

## Deviations from Plan

None -- plan executed exactly as written.

## Commits

| Hash | Description |
|------|-------------|
| faf49fa | feat(23-02): create device lifecycle ConfigMap fixtures |
| 78f3038 | feat(23-02): create device lifecycle scenario scripts 21-23 |
