---
phase: 11
plan: 03
subsystem: configuration
tags: [oid-map, jsonc, integration-tests, config-binding]
dependency-graph:
  requires: [11-01, 11-02]
  provides: [oid-map-test-coverage, config-cleanup]
  affects: [12-device-simulation]
tech-stack:
  added: []
  patterns: [ConfigurationBuilder-integration-test, JSONC-config-validation]
key-files:
  created:
    - tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
  modified:
    - src/SnmpCollector/config/oidmap-obp.json
    - src/SnmpCollector/appsettings.Development.json
decisions:
  - id: DEC-11-03-01
    decision: "Wrap oidmap-obp.json entries in OidMap section for config binding compatibility"
    why: "Root-level keys don't bind to GetSection('OidMap'); wrapping ensures correct merge"
metrics:
  duration: ~5 min
  completed: 2026-03-07
---

# Phase 11 Plan 03: OID Map Auto-Scan Tests and Config Cleanup Summary

Integration tests for OID map auto-scan with JSONC parsing validation and appsettings cleanup.

## What Was Done

### Task 1: OidMapAutoScanTests (5 tests)

Created `OidMapAutoScanTests` class with 5 integration tests:

1. **LoadsOidMapFromJsoncFile** -- Verifies `//` comments in JSONC files don't cause parse errors when loaded via `ConfigurationBuilder.AddJsonFile`.
2. **MergesMultipleOidMapFiles** -- Creates two temp JSON files with different OidMap entries, loads both, confirms all entries merge into a single `OidMapOptions.Entries` dictionary.
3. **ObpOidMapHas24Entries** -- Loads the real `oidmap-obp.json`, asserts exactly 24 entries (4 links x 6 metrics), spot-checks `obp_link_state_L1`, `obp_r4_power_L4`, `obp_channel_L2`.
4. **ObpOidNamingConventionIsConsistent** -- All 24 metric names match regex `^obp_(link_state|channel|r[1-4]_power)_L[1-4]$`.
5. **ObpOidStringsFollowEnterprisePrefix** -- All OID keys start with `1.3.6.1.4.1.47477.10.21.` and end with `.0`.

### Task 2: appsettings.Development.json Cleanup

Replaced inline OidMap entries with `"OidMap": {}`. OIDs now come exclusively from `oidmap-*.json` files via config auto-scan.

### Task 3: Full Build and Test Verification

All 126 tests pass. Both projects build with zero warnings.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] oidmap-obp.json missing OidMap section wrapper**

- **Found during:** Task 1 (ObpOidMapHas24Entries returned 0 entries)
- **Issue:** OID entries were at root level in oidmap-obp.json. When loaded via `AddJsonFile`, root-level keys don't appear under `GetSection("OidMap")`, so `OidMapOptions.Entries` would always be empty.
- **Fix:** Wrapped all 24 entries inside `"OidMap": { ... }` to match the production binding pattern in `ServiceCollectionExtensions.AddSnmpConfiguration`.
- **Files modified:** `src/SnmpCollector/config/oidmap-obp.json`
- **Commit:** 00f9528

## Commits

| Hash    | Message                                                            |
|---------|--------------------------------------------------------------------|
| 00f9528 | test(11-03): add OidMapAutoScanTests for JSONC parsing and OBP OID verification |
| 30b1dd2 | chore(11-03): empty OidMap section in appsettings.Development.json |

## Test Results

- 126 total tests, 0 failures, 0 skipped
- 5 new tests in OidMapAutoScanTests

## Next Phase Readiness

Phase 11 complete. OID map infrastructure is fully tested and production-ready:
- JSONC parsing works with .NET ConfigurationBuilder
- Multi-file merge verified (future device types can add oidmap-*.json files)
- All 24 OBP OIDs resolve correctly through OidMapOptions binding
- appsettings.Development.json cleaned up (no inline OID conflicts)
