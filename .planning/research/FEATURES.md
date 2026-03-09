# Feature Landscape: E2E System Verification

**Domain:** E2E test scenarios for SNMP monitoring pipeline (SnmpCollector -> OTel -> Prometheus)
**Researched:** 2026-03-09
**Confidence:** HIGH -- derived directly from codebase analysis of shipped v1.0-v1.3 features

---

## Table Stakes

Test scenarios that MUST pass for the system to be considered working. Each maps to a shipped feature that has no E2E verification yet.

### Category 1: Pipeline Counter Verification

All 10 pipeline counters must be provably incrementing in Prometheus with correct `device_name` labels.

| Test Scenario | What It Proves | Complexity | Depends On | Prometheus Query |
|---------------|----------------|------------|------------|------------------|
| TC-01: snmp_poll_executed_total increments | MetricPollJob fires and completes poll cycles | LOW | Existing OBP/NPB simulators running | `snmp_poll_executed_total{device_name="OBP-01"}` |
| TC-02: snmp_event_published_total increments | ChannelConsumerService dispatches trap varbinds into MediatR pipeline | LOW | OBP simulator sending traps | `snmp_event_published_total{device_name="OBP-01"}` |
| TC-03: snmp_event_handled_total increments | OtelMetricHandler successfully processes notifications (including heartbeat) | LOW | OBP/NPB simulators + HeartbeatJob | `snmp_event_handled_total` |
| TC-04: snmp_trap_received_total increments | ChannelConsumerService counts each trap varbind consumed from channel | LOW | OBP simulator sending traps | `snmp_trap_received_total{device_name="OBP-01"}` |
| TC-05: snmp_gauge exists with correct labels | RecordGauge produces metric_name, oid, device_name, ip, source, snmp_type labels | MEDIUM | Leader pod exporting + simulators | `snmp_gauge{device_name="OBP-01"}` |
| TC-06: snmp_info exists with correct labels | RecordInfo produces metric_name, oid, device_name, ip, source, snmp_type, value labels | MEDIUM | Leader pod exporting + OBP NMU OIDs | `snmp_info{device_name="OBP-01"}` |
| TC-07: Heartbeat does NOT appear in snmp_gauge/snmp_info | IsHeartbeat flag causes OtelMetricHandler to skip metric export | LOW | HeartbeatJob running | `snmp_gauge{metric_name=~".*heartbeat.*"}` should return empty |
| TC-08: snmp_event_published > snmp_event_handled (or equal) | Published >= handled proves no events silently bypass the pipeline | LOW | Running system with traffic | Compare two counters |
| TC-09: snmp_poll_executed_total for NPB-01 | Second device also has poll activity | LOW | NPB simulator running | `snmp_poll_executed_total{device_name="NPB-01"}` |
| TC-10: Pipeline counters have device_name label | All counters carry device_name tag (verified in PipelineMetricService code) | LOW | Any traffic | `snmp_event_handled_total` group by device_name |

**Notes:**
- TC-05 and TC-06 require the querying pod to be the leader (MetricRoleGatedExporter gates business metrics). Verification approach: query Prometheus directly since leader exports on scrape cycle.
- TC-07 is a negative test: absence of data. The heartbeat device name is `__heartbeat__` (from HeartbeatJobOptions.HeartbeatDeviceName).
- TC-08 is a consistency check. If published < handled, something is generating notifications outside the normal path.

### Category 2: Business Metric Correctness

The two business instruments (snmp_gauge, snmp_info) must carry correct SNMP type codes and resolve OIDs to metric names.

| Test Scenario | What It Proves | Complexity | Depends On |
|---------------|----------------|------------|------------|
| TC-11: Integer32 values appear with snmp_type="integer32" | OtelMetricHandler switch on SnmpType.Integer32 works | LOW | OBP link_state/channel OIDs (Integer32) |
| TC-12: Counter64 values appear with snmp_type="counter64" | OtelMetricHandler switch on SnmpType.Counter64 works | LOW | NPB traffic counter OIDs |
| TC-13: OctetString values appear as snmp_info with value label | RecordInfo path works, string value truncation at 128 chars | LOW | OBP NMU info OIDs (OctetString) |
| TC-14: source="poll" label on polled metrics | MetricPollJob sets Source=Poll | LOW | Any polled metric |
| TC-15: source="trap" label on trapped metrics | ChannelConsumerService sets Source=Trap | LOW | OBP StateChange trap |
| TC-16: metric_name resolves to human-readable name | OidResolutionBehavior resolves via OidMapService | LOW | OID map loaded with OBP/NPB entries |
| TC-17: Gauge32, TimeTicks types produce snmp_gauge | All numeric types route to RecordGauge | MEDIUM | Test simulator with these types |

**Notes:**
- Counter32 is used by OBP/NPB but is recorded as a gauge (Prometheus rate() handles delta). The snmp_type label distinguishes it.
- TC-17 requires the test simulator because existing simulators only produce Integer32, Counter64, and OctetString.

### Category 3: Unknown OID Handling

Unmapped OIDs must resolve to "Unknown" metric_name and still flow through the pipeline.

| Test Scenario | What It Proves | Complexity | Depends On |
|---------------|----------------|------------|------------|
| TC-18: Unmapped poll OID resolves to metric_name="Unknown" | OidMapService.Resolve returns "Unknown" for missing OIDs | LOW | Test simulator with OID not in oidmaps.json |
| TC-19: Unmapped trap OID resolves to metric_name="Unknown" | Same path via trap ingestion | LOW | Test simulator sending trap with unmapped OID |
| TC-20: Unknown OIDs still increment snmp_event_handled_total | Pipeline does not reject unknown OIDs (OidResolutionBehavior always calls next()) | LOW | Verify counter after unknown OID arrives |
| TC-21: Unknown OIDs appear in snmp_gauge/snmp_info | Business metrics recorded even with metric_name="Unknown" | LOW | Query `snmp_gauge{metric_name="Unknown"}` |

**Notes:**
- These tests are critical because "Unknown" is the discovery mechanism. Operators filter Grafana for metric_name="Unknown" to find unmapped OIDs.
- The OidResolutionBehavior never short-circuits -- it logs at Debug level and continues. This is by design.

---

## Differentiators

Edge case tests that reveal hidden bugs. These go beyond "does it work" to "does it work under mutation and stress."

### Category 4: Business Metric Mutations (OID Map Hot-Reload)

These test the OidMapWatcherService -> OidMapService.UpdateMap -> FrozenDictionary atomic swap path.

| Test Scenario | What It Reveals | Complexity | Depends On |
|---------------|-----------------|------------|------------|
| TC-22: Rename OID mapping -> metric_name changes in Prometheus | OidMapService.UpdateMap atomic swap takes effect on next poll/trap | MEDIUM | kubectl edit ConfigMap simetra-oidmaps |
| TC-23: Remove OID mapping -> metric_name reverts to "Unknown" | Removed OID falls through to OidMapService.Unknown constant | MEDIUM | kubectl edit ConfigMap simetra-oidmaps |
| TC-24: Add new OID mapping -> previously-Unknown OID gets name | New entry in map resolves OID that was previously unmapped | MEDIUM | kubectl edit ConfigMap simetra-oidmaps |
| TC-25: Rapid successive OID map changes -> no crash | SemaphoreSlim serialization in OidMapWatcherService prevents race | MEDIUM | Multiple rapid ConfigMap edits |
| TC-26: OID map change logged with diff (added/removed/changed) | OidMapService.UpdateMap logs structured diff | LOW | kubectl logs after ConfigMap edit |

**Notes:**
- TC-22 is the most important mutation test. After renaming `obp_r1_power_L1` to `obp_r1_power_L1_renamed`, the OLD metric_name should stop appearing in new samples and the NEW name should appear.
- Prometheus retains old time series for its retention period, so the old name won't disappear -- but new samples should only carry the new name. Verification: query with a recent time window (last 30s).
- TC-25 tests the SemaphoreSlim gate. The watcher deserializes JSON and calls UpdateMap under the lock.

### Category 5: Device Lifecycle (Device Config Hot-Reload)

These test DeviceWatcherService -> DeviceRegistry.ReloadAsync -> DynamicPollScheduler.ReconcileAsync.

| Test Scenario | What It Reveals | Complexity | Depends On |
|---------------|-----------------|------------|------------|
| TC-27: Add new device -> poll jobs created, metrics appear | DynamicPollScheduler adds Quartz jobs for new device | HIGH | Test simulator as new device + ConfigMap edit |
| TC-28: Remove device -> poll jobs removed, no new metrics | DynamicPollScheduler removes Quartz jobs, liveness vector cleaned | HIGH | Remove device from ConfigMap |
| TC-29: Change poll interval -> Quartz trigger rescheduled | DynamicPollScheduler reschedules via JobIntervalRegistry comparison | MEDIUM | Change intervalSeconds in ConfigMap |
| TC-30: Add device with unreachable IP -> snmp_poll_unreachable_total fires | DeviceUnreachabilityTracker marks device after 3 consecutive failures | MEDIUM | Add device pointing to non-existent IP |
| TC-31: Device recovers after being unreachable -> snmp_poll_recovered_total fires | DeviceUnreachabilityTracker.RecordSuccess returns true on transition | MEDIUM | Start simulator after unreachable threshold hit |

**Notes:**
- TC-27 is the highest complexity test because it requires: (1) test simulator running on a known IP/port, (2) devices.json ConfigMap update with new device entry, (3) waiting for DynamicPollScheduler reconciliation, (4) verifying new metrics appear in Prometheus.
- TC-28 negative verification: after device removal, `snmp_poll_executed_total{device_name="removed-device"}` should stop incrementing (no new samples in recent window).
- TC-30 requires the threshold of 3 consecutive failures (hardcoded in DeviceUnreachabilityTracker). Test must wait for 3 poll cycles.

### Category 6: ConfigMap Watcher Resilience

These test the K8s API watch loop behavior under error conditions.

| Test Scenario | What It Reveals | Complexity | Depends On |
|---------------|-----------------|------------|------------|
| TC-32: Invalid JSON in oidmaps.json -> previous map retained | JsonException caught in HandleConfigMapChangedAsync, skips reload | MEDIUM | kubectl edit with malformed JSON |
| TC-33: Invalid JSON in devices.json -> previous devices retained | Same pattern in DeviceWatcherService | MEDIUM | kubectl edit with malformed JSON |
| TC-34: Missing key in ConfigMap -> warning logged, no crash | `configMap.Data.TryGetValue` returns false, early return | LOW | Remove oidmaps.json key from ConfigMap |
| TC-35: Null deserialized result -> warning logged, no crash | `oidMap is null` guard in HandleConfigMapChangedAsync | LOW | Set key to `null` in ConfigMap |
| TC-36: Watch reconnection after disconnect | Watch loop catches exception, waits 5s, reconnects | HIGH | Hard to trigger naturally; verify via log pattern |

**Notes:**
- TC-32 and TC-33 are the most valuable resilience tests. A typo in a ConfigMap edit should never crash the collector.
- TC-36 is hard to test deterministically. The K8s API server closes watch connections after ~30 minutes. Verification approach: check logs for "watch connection closed, reconnecting" pattern. This happens naturally over time.
- For TC-32/TC-33, after fixing the JSON, the watcher should detect the Modified event and reload successfully.

### Category 7: Community String Authentication

| Test Scenario | What It Reveals | Complexity | Depends On |
|---------------|-----------------|------------|------------|
| TC-37: Trap with wrong community string -> snmp_trap_auth_failed_total increments | CommunityStringHelper.TryExtractDeviceName returns false, counter fired | MEDIUM | Test simulator with wrong community |
| TC-38: Trap with valid Simetra.{name} community -> processed normally | CommunityStringHelper extracts device name correctly | LOW | Existing simulator already proves this |
| TC-39: Trap with "public" community -> auth failed (not Simetra.* prefix) | Standard default community rejected | LOW | Test simulator with community="public" |

### Category 8: Leader Election Gating

| Test Scenario | What It Reveals | Complexity | Depends On |
|---------------|-----------------|------------|------------|
| TC-40: Only leader exports snmp_gauge/snmp_info | MetricRoleGatedExporter filters SnmpCollector.Leader meter on followers | HIGH | 3-replica deployment, identify leader |
| TC-41: All replicas export pipeline counters | Pipeline counters on SnmpCollector meter pass through regardless of role | MEDIUM | Query pipeline counters grouped by pod |
| TC-42: Leader failover -> new leader exports business metrics | K8s Lease handoff works; new leader's MetricRoleGatedExporter starts passing | HIGH | Kill leader pod, verify new leader exports |

**Notes:**
- TC-40 and TC-41 together prove the gating logic. Pipeline counters (SnmpCollector meter) should have 3x pod labels. Business metrics (SnmpCollector.Leader meter) should come from exactly one pod.
- TC-42 is the most complex test in the entire suite. It requires killing a specific pod and verifying continuity. This may be too disruptive for automated E2E and could be manual-only.

---

## Anti-Features

Tests NOT to build, and why.

| Anti-Test | Why It Seems Useful | Why NOT to Build | What to Do Instead |
|-----------|---------------------|------------------|-------------------|
| Automated pod kill / chaos testing | "Verify failover automatically" | Pod kills affect the entire cluster state, interfere with other tests, and are hard to make idempotent. Chaos testing is a separate discipline with dedicated tools (Litmus, Chaos Mesh). | Document TC-42 as a manual verification step. Run it once, record evidence, don't automate it in the E2E suite. |
| Performance / load testing | "How many traps/sec can the pipeline handle?" | Performance testing requires dedicated infrastructure, baseline establishment, and statistical analysis. It's a separate effort from functional E2E verification. | Note as a future milestone. The E2E suite verifies correctness, not throughput. |
| Grafana dashboard rendering tests | "Verify dashboards show data correctly" | Grafana rendering requires browser automation (Playwright/Selenium), which is a separate testing domain. The E2E suite verifies data in Prometheus; Grafana is a visualization layer. | Verify Prometheus has correct data. Dashboard correctness is a visual inspection task. |
| SNMP v3 authentication tests | "Test v3 auth/encryption paths" | The system only supports v2c (explicit constraint). Testing v3 would test unsupported functionality. | Out of scope per project constraints. |
| Embedded TSDB query tests | "Query metrics directly from the collector" | The system has no embedded TSDB. Metrics flow through OTel to Prometheus. Testing anything other than Prometheus queries tests the wrong thing. | All verification through Prometheus HTTP API only. |
| Unit test duplication as E2E | "Re-run unit test assertions against live system" | 138 unit tests already cover component-level behavior. E2E tests should verify integration, not re-test units. | E2E tests focus on cross-component data flow: SNMP -> pipeline -> OTel -> Prometheus. |
| Exhaustive OID coverage tests | "Test every one of the 92 OIDs individually" | 92 individual OID assertions would be brittle and slow. The OID map is a flat dictionary; if one OID resolves, the mechanism works for all. | Test a representative sample: one Integer32, one Counter64, one OctetString, one unmapped OID. |
| Network partition simulation | "What if the OTel collector is unreachable?" | Network partition testing requires infrastructure-level manipulation (iptables rules, network policies). Too complex for a kubectl-based E2E suite. | Document as a known risk. The OTel SDK has retry/backoff built in. |
| ConfigMap delete tests | "What if someone deletes the entire ConfigMap?" | Both watchers already handle WatchEventType.Deleted by logging a warning and retaining current state (verified in code). The behavior is simple enough to trust from code review. | Note the behavior in the report. No E2E test needed -- the code path is 3 lines with a log statement. |

---

## Feature Dependencies

```
[Test Simulator]
    |-- required by --> TC-17 (Gauge32, TimeTicks types)
    |-- required by --> TC-18, TC-19 (unmapped OIDs for poll and trap)
    |-- required by --> TC-27 (new device lifecycle)
    |-- required by --> TC-30, TC-31 (unreachable device)
    |-- required by --> TC-37, TC-39 (wrong community string traps)

[Existing OBP Simulator]
    |-- sufficient for --> TC-01 through TC-06 (pipeline counters)
    |-- sufficient for --> TC-11, TC-13, TC-14, TC-15, TC-16 (type correctness)
    |-- sufficient for --> TC-22 through TC-26 (OID map mutations)

[Existing NPB Simulator]
    |-- sufficient for --> TC-09 (second device poll activity)
    |-- sufficient for --> TC-12 (Counter64 type)

[kubectl + Prometheus HTTP API]
    |-- required by --> ALL test scenarios (verification method)

[3-Replica Deployment]
    |-- required by --> TC-40, TC-41, TC-42 (leader election gating)
```

### Dependency Summary

The test simulator is required for 9 of the 42 test scenarios. The remaining 33 can use existing infrastructure. This means:

1. **Phase 1 (no test simulator):** Run TC-01 through TC-16, TC-22 through TC-26, TC-38 -- 22 tests using existing simulators only.
2. **Phase 2 (with test simulator):** Run TC-17 through TC-21, TC-27 through TC-31, TC-37, TC-39 -- 12 tests requiring the dedicated simulator.
3. **Phase 3 (watcher resilience):** Run TC-32 through TC-36 -- 5 tests requiring ConfigMap manipulation.
4. **Phase 4 (leader election):** Run TC-40 through TC-42 -- 3 tests requiring multi-replica awareness.

---

## Test Simulator Requirements

Based on the test scenarios that cannot use existing OBP/NPB simulators:

| Capability | Required By | Why Existing Simulators Cannot Cover |
|------------|-------------|--------------------------------------|
| Serve OIDs NOT in oidmaps.json | TC-18, TC-19, TC-21 | OBP/NPB simulators serve only mapped OIDs |
| Serve Gauge32 and TimeTicks types | TC-17 | OBP uses Integer32; NPB uses Counter64; neither produces Gauge32/TimeTicks |
| Send traps with wrong community string | TC-37, TC-39 | OBP/NPB simulators hardcode Simetra.{DeviceName} community |
| Be addable/removable as a device | TC-27, TC-28, TC-29 | OBP/NPB are permanent fixtures; test simulator is ephemeral |
| Be startable/stoppable on demand | TC-30, TC-31 | OBP/NPB run continuously; test simulator needs controlled lifecycle |
| Use configurable community string | TC-37, TC-39 | Must send traps with arbitrary community strings |

**Recommendation:** Single Python script using pysnmp (same stack as existing simulators). Configurable via environment variables: DEVICE_NAME, COMMUNITY, SERVE_UNMAPPED_OIDS (bool), SNMP_TYPES (list of types to serve). Deployed as a K8s pod but can be started/stopped via `kubectl scale`.

---

## Complexity Assessment

| Complexity | Count | Test IDs |
|------------|-------|----------|
| LOW | 19 | TC-01, TC-02, TC-03, TC-04, TC-07, TC-08, TC-09, TC-10, TC-11, TC-12, TC-13, TC-14, TC-15, TC-16, TC-18, TC-19, TC-20, TC-34, TC-35 |
| MEDIUM | 16 | TC-05, TC-06, TC-17, TC-21, TC-22, TC-23, TC-24, TC-25, TC-26, TC-29, TC-30, TC-31, TC-32, TC-33, TC-37, TC-41 |
| HIGH | 7 | TC-27, TC-28, TC-36, TC-39, TC-40, TC-42 |

**Total: 42 test scenarios across 8 categories.**

---

## MVP Test Recommendation

For the E2E verification milestone, prioritize in this order:

**Must run (28 tests):** Categories 1-4 (pipeline counters, business metric correctness, unknown OID handling, OID map mutations). These verify the core data flow from SNMP to Prometheus.

**Should run (8 tests):** Categories 5-6 (device lifecycle, ConfigMap watcher resilience). These verify operational reliability under configuration changes.

**Run if time permits (6 tests):** Categories 7-8 (community string auth, leader election gating). These verify security and HA behaviors that are already well-covered by unit tests (138 existing tests).

**Explicitly skip:** All anti-features listed above. Document the rationale in the test report.

---

## Sources

- Codebase analysis: `src/SnmpCollector/Telemetry/PipelineMetricService.cs` -- all 10 pipeline counter definitions (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` -- SnmpType switch, heartbeat suppression, RecordGauge/RecordInfo paths (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/OidMapService.cs` -- FrozenDictionary atomic swap, UpdateMap diff logging, Unknown constant (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Services/OidMapWatcherService.cs` -- K8s watch loop, SemaphoreSlim, JsonException handling, reconnect logic (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Services/DeviceWatcherService.cs` -- device reload, DynamicPollScheduler reconciliation (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Services/SnmpTrapListenerService.cs` -- community string validation, TrapAuthFailed counter (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/DeviceUnreachabilityTracker.cs` -- 3-failure threshold, transition detection (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` -- meter-name gating, follower filtering (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` -- snmp_gauge and snmp_info instrument creation, label sets (HIGH confidence)
- Codebase analysis: `simulators/obp/obp_simulator.py` -- OBP OID set, trap behavior, community string convention (HIGH confidence)

---
*Feature research for: E2E system verification of SNMP monitoring pipeline*
*Researched: 2026-03-09*
