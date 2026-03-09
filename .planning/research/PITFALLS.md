# Domain Pitfalls

**Domain:** E2E system verification for SNMP monitoring pipeline (OTel push to Prometheus via remote write)
**Researched:** 2026-03-09
**Confidence:** HIGH (verified against system source code, OTel SDK configuration, Prometheus remote write spec, K8s API watch behavior)

---

## Critical Pitfalls

Mistakes that produce false pass/fail results or make the test suite unreliable.

### Pitfall 1: OTel Export Interval Creates a 15-Second Blind Spot

**What goes wrong:**
Tests query Prometheus immediately after triggering a simulator action (trap, config change) and find no data. The test reports a failure that is actually a timing issue. The SnmpCollector uses a `PeriodicExportingMetricReader` with `exportIntervalMilliseconds: 15_000`. After a metric is recorded in the OTel SDK, it will not be pushed to the OTel Collector until the next 15-second export cycle. Add the OTel Collector's own batching/flush interval and you get a worst-case latency of ~16-18 seconds from metric record to Prometheus queryability.

**Why it happens:**
Developers think "metric was recorded" means "metric is in Prometheus." The push path is: App SDK (15s batch) -> OTel Collector (OTLP receiver) -> prometheusremotewrite exporter -> Prometheus. Each hop adds latency.

**Consequences:**
- False negatives: tests fail because they query too early
- Flaky tests: sometimes the export aligns with the query, sometimes it doesn't
- Over-engineering: developers add 60-second sleeps everywhere "just in case"

**Prevention:**
Use a **poll-until-satisfied** pattern with a maximum timeout rather than fixed sleeps:
```
Wait up to 30 seconds, polling Prometheus every 3 seconds, until the expected metric appears.
If 30 seconds pass without match, report failure with the last query result for diagnosis.
```
The 30-second ceiling is derived from: 15s max export interval + 5s OTel Collector flush margin + 10s safety buffer. For counter metrics (cumulative), wait for the value to be >= expected, not == expected, since additional increments may arrive between checks.

**Detection:**
- Tests that pass locally but fail in CI (different timing)
- Tests that pass when run individually but fail in batch (export cycle alignment shifts)
- Hardcoded `sleep(60)` calls in test code

**Phase to address:** Test harness/framework setup -- establish the polling utility before writing any verification scenarios.

---

### Pitfall 2: Metric Staleness Does Not Work as Expected with OTel Remote Write

**What goes wrong:**
After removing a device or OID from configuration, the test queries Prometheus expecting the old metric to disappear. It does not. The metric persists with its last known value for approximately 5 minutes. The test either (a) waits 5+ minutes causing unacceptable test duration, or (b) falsely concludes removal failed.

**Why it happens:**
The OTel `prometheusremotewrite` exporter does not emit Prometheus staleness markers (stale NaN = `0x7ff0000000000002`) when a metric series stops being reported by a non-Prometheus source. This is a documented limitation: staleness markers are Prometheus-specific, and OTLP sources do not generate the `NoRecordedValue` flag that would trigger them. The OTel Collector continues sending the last known value for up to 5 minutes after the source stops reporting the metric.

Additionally, Prometheus itself applies a 5-minute lookback window (`lookback_delta`) by default. Even if the metric truly stops being written, `last_over_time()` and instant queries will return the stale value for up to 5 minutes.

**Consequences:**
- Cannot verify metric removal via "query returns no data" within a reasonable test window
- False confidence that metrics were removed when they were just outside the lookback window
- Tests that take 5+ minutes per removal scenario

**Prevention:**
Do NOT verify metric removal by checking for absence in Prometheus queries. Instead:

1. **Verify via log evidence:** After removing an OID from the map, confirm via `kubectl logs` that subsequent polls produce `metric_name=Unknown` for that OID (the OidMapService resolves unmapped OIDs to "Unknown"). This is observable within one poll interval (10 seconds).

2. **Verify via metric_name label change:** Query for `snmp_gauge{metric_name="Unknown",oid="<the-removed-oid>"}` appearing -- this proves the OID is no longer mapped, which is the actual system behavior being tested.

3. **Verify device removal via counter stagnation:** After removing a device, verify that `snmp_poll_executed_total{device_name="<removed>"}` stops incrementing (compare two readings 15+ seconds apart). Do not check for series absence.

4. **Accept the 5-minute staleness window as a known system characteristic**, not a bug.

**Detection:**
- Tests with 5+ minute waits labeled "waiting for staleness"
- Tests asserting `result == empty` for removed metrics
- Flaky tests that pass after long delays but fail with short timeouts

**Phase to address:** Test design phase -- establish removal verification patterns using log evidence and label changes before writing removal scenarios.

---

### Pitfall 3: Leader Election Timing Makes Business Metric Verification Non-Deterministic

**What goes wrong:**
The test queries `snmp_gauge` or `snmp_info` from Prometheus and gets no results, even though the simulator is running and polls are executing. The cause: no pod currently holds leadership, or a leadership transition is in progress, so the `MetricRoleGatedExporter` suppresses all business metrics from export.

**Why it happens:**
Leader election uses K8s Lease API with configurable durations. During the window between one leader releasing the lease and another acquiring it, no business metrics are exported. The lease configuration (`LeaseDuration`, `RenewDeadline`, `RetryPeriod`) determines the gap. On pod restart or crash, the gap can be seconds to tens of seconds depending on TTL expiry vs. explicit lease deletion.

In the current system, `GracefulShutdownService` explicitly deletes the lease on graceful shutdown (near-instant failover). But if a pod is killed forcefully, followers must wait for the full `LeaseDuration` to expire.

**Consequences:**
- Business metric queries return empty during leadership transitions
- Tests checking `snmp_gauge` immediately after cluster changes fail sporadically
- Pipeline metrics (snmp.event.handled) work fine, creating confusion about why business metrics are missing

**Prevention:**
1. **Always verify leader exists before testing business metrics:** Query pipeline metrics first (`snmp_event_handled_total`) -- these export from ALL instances regardless of leadership. If pipeline metrics are flowing but `snmp_gauge` is empty, it is a leadership gap.

2. **Check leadership status via logs:** `kubectl logs -l app=snmp-collector -n simetra | grep "Acquired leadership"` -- confirm which pod holds leadership before running business metric tests.

3. **Wait for leadership stabilization after any cluster change:** After scaling, restarting, or config changes that cause pod restarts, wait for lease acquisition log evidence plus one full OTel export cycle (15s) before querying business metrics.

4. **Query with `service_instance_id` label** to verify the leader pod specifically is exporting.

**Detection:**
- `snmp_gauge` queries return empty while `snmp_event_handled_total` is incrementing
- Tests pass when run alone but fail after pod restart scenarios
- Intermittent "no data" results that resolve after re-running

**Phase to address:** Pre-test health check phase -- verify leadership state as a precondition before every business metric test scenario.

---

### Pitfall 4: Test Isolation Failure -- Previous Test Metrics Contaminate Next Test

**What goes wrong:**
Test scenario B queries Prometheus and finds metrics from test scenario A still present. This causes false positives (metrics appear to exist when they should not) or incorrect count assertions (counter values include increments from the previous test).

**Why it happens:**
Prometheus is an append-only time series database. Once a metric is written, it persists for the configured retention period (30 days in this system). There is no practical way to "reset" Prometheus between tests without losing all data. Combined with the 5-minute staleness window (Pitfall 2), metrics from a previous test are indistinguishable from current metrics unless timestamps are carefully compared.

Additionally, OTel counters use cumulative temporality. `snmp_event_handled_total` never resets to zero between tests -- it monotonically increases across the pod's lifetime. A test that asserts "counter equals 5" will fail if a previous test already incremented it to 12.

**Consequences:**
- Tests must be run in a specific order to pass
- Tests cannot be run independently for debugging
- Counter value assertions are fragile and break when test execution order changes

**Prevention:**
1. **Never assert counter absolute values.** Instead, record the counter value before the test action, perform the action, wait, then assert the delta: `post_value - pre_value >= expected_increment`.

2. **Use timestamp-bounded queries.** Record `start_time` before the test action. Query with `snmp_gauge{...} @ <timestamp>` or use range queries with `[30s]` windows anchored to the test execution window.

3. **Use unique label values per test scenario.** If testing OID map changes, use OID values or metric names specific to that test scenario. If two tests both use the same OID, their metrics will collide.

4. **For the dedicated test simulator**, use unique device_name and community string per test scenario to naturally isolate metric series by label.

**Detection:**
- Tests that pass on first run but fail on second run without cluster restart
- Counter assertions with exact values that break when test order changes
- Test failures that reference metrics with timestamps older than the test start time

**Phase to address:** Test design phase -- establish delta-based counter assertions and timestamp-bounded queries as foundational patterns.

---

## Moderate Pitfalls

Mistakes that cause delays, flaky tests, or missed coverage.

### Pitfall 5: ConfigMap Propagation is Not Instantaneous Across All Replicas

**What goes wrong:**
A test applies a ConfigMap change (OID map update, device addition) and immediately checks all 3 replicas for the new behavior. Some replicas have not received the watch event yet, causing partial failures.

**Why it happens:**
The system uses K8s API watch (not volume-projected ConfigMaps) via `OidMapWatcherService` and `DeviceWatcherService`. K8s API watch events are typically sub-second, but:
- The watch event must be delivered to each pod's watcher independently
- Each watcher serializes reload via `SemaphoreSlim` (one at a time)
- The `DynamicPollScheduler.ReconcileAsync` must complete (Quartz job registration)
- If a watch connection was recently closed (~30 min K8s server timeout), the pod reconnects with a 5-second backoff

In practice, all 3 replicas typically converge within 1-3 seconds, but edge cases can extend this to 5-10 seconds.

**Prevention:**
1. After applying a ConfigMap change via `kubectl apply`, wait for log evidence from ALL replicas: `"OID map reload complete"` or `"Device config reload complete"` with the expected entry count.
2. Add a 5-second post-reload stabilization wait before querying Prometheus, to allow the first post-reload poll cycle to complete and the 15-second export interval to fire.
3. For tests that verify behavior on a specific pod, filter logs by pod name rather than checking all replicas.

**Detection:**
- Tests that pass 2 out of 3 times (one replica slow to update)
- Log output showing reload on 2 of 3 pods before the test query fires

**Phase to address:** ConfigMap mutation test scenarios -- add per-pod log verification as a precondition after every ConfigMap apply.

---

### Pitfall 6: Querying Prometheus Counter Rate Over Too Short a Window

**What goes wrong:**
Test uses `rate(snmp_event_handled_total[15s])` and gets zero or `NaN` because there are fewer than 2 data points in the 15-second window. The test concludes the pipeline is not processing events.

**Why it happens:**
Prometheus `rate()` requires at least 2 data points within the specified range to compute a rate. With a 15-second OTel export interval, a `[15s]` window may contain only 1 data point. The rule of thumb is that the range window should be at least 2x the scrape/write interval.

For this system (15s export), `rate(...[30s])` is the minimum viable window, and `rate(...[1m])` is safer.

**Prevention:**
1. **For counter verification, prefer raw value comparison over rate().** Query `snmp_event_handled_total{...}` directly, compare before/after values.
2. If rate() is needed, use at least `[1m]` window: `rate(snmp_event_handled_total[1m])`.
3. Never use `irate()` for E2E test assertions -- it uses only the last two data points and is highly volatile.

**Detection:**
- `rate()` queries returning NaN or 0 when the counter is clearly incrementing (visible via raw value query)
- Different results depending on when within the export cycle the query runs

**Phase to address:** Test harness setup -- document approved PromQL patterns for test assertions.

---

### Pitfall 7: UDP Trap Delivery is Unreliable by Design

**What goes wrong:**
A test triggers a trap from a simulator, then checks Prometheus for the resulting metric. The metric never appears. The trap was lost on the network (UDP has no delivery guarantee) and there is no retry mechanism.

**Why it happens:**
SNMP traps use UDP. Within a K8s cluster, UDP packet loss is rare but not zero, especially under resource contention. The SnmpTrapListenerService listens on port 10162 and has no acknowledgment protocol. If the pod is temporarily unavailable (restart, OOM, CPU throttling), the trap is silently dropped.

**Prevention:**
1. **For trap-based test scenarios, always verify receipt via logs** before checking Prometheus: `"Trap received from {IP}"` or `snmp_trap_received_total` counter increment.
2. **If trap verification fails, retry the trap send** (up to 3 times with 2-second intervals) before declaring failure.
3. **Prefer poll-based verification where possible** -- polls are initiated by the collector and have timeout/retry semantics, making them more deterministic than traps.
4. For the dedicated test simulator, log trap send confirmations on the simulator side to correlate with collector receipt.

**Detection:**
- Sporadic trap test failures that pass on retry
- Missing `snmp_trap_received_total` increment despite simulator confirming send
- Trap tests that work in low-load environments but fail under stress

**Phase to address:** Trap verification scenarios -- build retry logic into trap send utilities.

---

### Pitfall 8: Verifying Pipeline Counters on Wrong Pod Due to Leader-Gating Confusion

**What goes wrong:**
A test verifies that `snmp_event_handled_total` incremented on the leader pod, but queries Prometheus filtered to a specific `service_instance_id`. The query returns zero because the test is looking at a follower pod. The tester concludes the pipeline is broken.

**Why it happens:**
Pipeline metrics (`SnmpCollector` meter) are exported by ALL instances. Business metrics (`SnmpCollector.Leader` meter) are exported only by the leader. Testers confuse which metrics are gated and which are not.

Additionally, OTel resource attributes add `service_instance_id` and `k8s_pod_name` labels to ALL metrics (both pipeline and business). When querying pipeline counters, filtering by a specific pod's identity is valid. But for business metrics, only the leader's pod identity will have data.

**Prevention:**
Document and reference this lookup table in every test scenario:

| Metric | Exported By | Filter By Pod? |
|--------|------------|----------------|
| `snmp_event_handled_total` | All instances | Yes (each pod has its own count) |
| `snmp_poll_executed_total` | All instances | Yes |
| `snmp_trap_received_total` | All instances | Yes |
| `snmp_gauge` | Leader only | Only leader pod will have data |
| `snmp_info` | Leader only | Only leader pod will have data |

For pipeline counter verification across the cluster, omit the pod identity filter and sum: `sum(snmp_event_handled_total{device_name="OBP-01"})`.

**Detection:**
- Queries returning data for some pods but not others
- Business metric queries that only work when filtered to one specific pod
- Confusion about why removing the pod filter changes results

**Phase to address:** Test harness documentation -- create a metric reference card before writing test scenarios.

---

### Pitfall 9: Heartbeat Metrics Contaminating Test Assertions

**What goes wrong:**
A test counts total `snmp_event_handled_total` increments after triggering a poll and finds more increments than expected. The extra increments come from the HeartbeatJob, which fires on a separate schedule and also increments `snmp_event_handled_total` (with `device_name` derived from the heartbeat's internal routing).

**Why it happens:**
The HeartbeatJob sends a loopback trap through the full MediatR pipeline. The `OtelMetricHandler` increments `snmp_event_handled_total` for heartbeats (with `IsHeartbeat=true` flag). Heartbeat events are correctly suppressed from `snmp_gauge`/`snmp_info` export, but pipeline counters still count them. The heartbeat runs independently on a timer, so its increments arrive at unpredictable times relative to the test.

**Prevention:**
1. **Always filter counter queries by `device_name`.** Heartbeat events use a specific device name (likely the pod's own identity or a sentinel value). Test scenarios should query `snmp_event_handled_total{device_name="OBP-01"}` rather than unfiltered `snmp_event_handled_total`.
2. **Use delta-based assertions** (Pitfall 4 prevention) so that background heartbeat increments between the "before" and "after" snapshots are excluded by the device_name filter.
3. **Verify heartbeat behavior is correct in a dedicated test**, not as a side effect of other tests.

**Detection:**
- Counter increments higher than expected by a small, varying amount
- Increments that occur even when no simulator is sending data
- The extra count matches the heartbeat interval pattern

**Phase to address:** Pipeline counter verification scenarios -- filter by device_name in all counter queries.

---

## Minor Pitfalls

Mistakes that cause annoyance or confusion but are fixable.

### Pitfall 10: kubectl logs Buffer Truncation Losing Evidence

**What goes wrong:**
A test checks `kubectl logs` for evidence of a specific event (OID map reload, trap receipt) but the log line has already scrolled out of the buffer. The test reports "no evidence found" when the event actually occurred.

**Why it happens:**
Default `kubectl logs` returns the last ~4096 lines or the current container's log buffer. In a high-throughput system with verbose logging, log lines from 30 seconds ago may already be truncated.

**Prevention:**
1. Use `kubectl logs --since=30s` to scope log retrieval to the test's time window.
2. Use `kubectl logs --timestamps` to correlate log entries with test execution time.
3. For critical evidence, capture logs to a file immediately after the action rather than querying later.
4. Consider querying Elasticsearch (the system exports logs via OTel Collector) for structured log search, though this adds another hop with its own latency.

**Detection:**
- Log evidence queries that return empty despite the action clearly succeeding (metric appeared in Prometheus)
- Inconsistent log evidence between fast and slow test runs

**Phase to address:** Test harness setup -- build log capture utilities with timestamp scoping.

---

### Pitfall 11: Port-Forward Instability During Long Test Runs

**What goes wrong:**
The test suite uses `kubectl port-forward svc/prometheus 9090:9090` to query Prometheus. Mid-suite, the port-forward drops silently. Subsequent Prometheus queries fail with connection refused, and the test reports metric verification failures.

**Prevention:**
1. Verify port-forward is alive before each Prometheus query attempt (quick health check: `GET /api/v1/status/buildinfo`).
2. Implement automatic port-forward reconnection in the test harness.
3. Alternatively, use `kubectl exec` to run `curl` against the in-cluster Prometheus service directly, bypassing port-forward entirely.

**Detection:**
- Tests that fail mid-suite with connection errors, not assertion errors
- Failures that correlate with test duration rather than test content

**Phase to address:** Test harness infrastructure -- build resilient Prometheus query utility.

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| Test harness setup | Fixed sleeps instead of poll-until-satisfied (Pitfall 1) | Build polling utility as first deliverable; 30s timeout, 3s poll interval |
| Pipeline counter verification | Heartbeat contamination (Pitfall 9), absolute value assertions (Pitfall 4) | Filter by device_name; use delta assertions |
| Business metric verification | Leader gap (Pitfall 3), staleness confusion (Pitfall 2) | Verify leadership first; verify via label changes not absence |
| OID map mutation tests | ConfigMap propagation delay (Pitfall 5), staleness (Pitfall 2) | Wait for reload logs from all pods; check for metric_name="Unknown" |
| Device lifecycle tests | Staleness prevents absence verification (Pitfall 2) | Verify via counter stagnation and log evidence |
| Trap-based scenarios | UDP unreliability (Pitfall 7), heartbeat noise (Pitfall 9) | Retry trap sends; filter by device_name |
| Test simulator design | Metric collision with production simulators (Pitfall 4) | Use unique device_name and community string per test scenario |
| ConfigMap watcher tests | All-replica convergence timing (Pitfall 5) | Per-pod log verification before metric queries |
| Multi-scenario test runs | Port-forward drops (Pitfall 11), log truncation (Pitfall 10) | Health-check port-forward; scope logs with --since |

---

## Key Timing Constants Reference

These are the actual values from the system source code and K8s manifests:

| Constant | Value | Source |
|----------|-------|--------|
| OTel export interval | 15 seconds | `ServiceCollectionExtensions.cs` line 105: `exportIntervalMilliseconds: 15_000` |
| Prometheus scrape interval | N/A (remote write, not scrape) | `prometheus.yaml`: uses `--web.enable-remote-write-receiver` |
| Prometheus staleness lookback | 5 minutes (default) | Prometheus default `lookback_delta` |
| Device poll interval | 10 seconds | `devices.json` configuration |
| Heartbeat interval | Configurable via `HeartbeatJobOptions` | `appsettings.json` |
| K8s watch reconnect backoff | 5 seconds | `OidMapWatcherService.cs` line 131: `TimeSpan.FromSeconds(5)` |
| K8s watch server timeout | ~30 minutes | K8s API server default |
| OTel metric temporality | Cumulative | `ServiceCollectionExtensions.cs` line 107: `TemporalityPreference = Cumulative` |
| Prometheus retention | 30 days | `prometheus.yaml`: `--storage.tsdb.retention.time=30d` |
| Graceful shutdown budget | 30 seconds | `deployment.yaml`: `terminationGracePeriodSeconds: 30` |
| Lease explicit delete | On graceful shutdown | `K8sLeaseElection.StopAsync()` deletes lease |

---

## Sources

- Prometheus Remote-Write 1.0 specification (staleness markers): [Prometheus Remote Write Spec](https://prometheus.io/docs/specs/prw/remote_write_spec/) -- HIGH confidence (official spec)
- Prometheus staleness behavior: [Staleness and PromQL - Robust Perception](https://www.robustperception.io/staleness-and-promql/) -- HIGH confidence (official Prometheus consulting)
- OTel prometheusremotewrite exporter staleness gap: [Issue #6620](https://github.com/open-telemetry/opentelemetry-collector-contrib/issues/6620) -- HIGH confidence (official OTel repo issue)
- OTel prometheusremotewrite keeps sending stale data: [Issue #27893](https://github.com/open-telemetry/opentelemetry-collector-contrib/issues/27893) -- HIGH confidence (official OTel repo issue, confirmed by maintainers)
- Prometheus remote write staleness compliance gap: [Issue #38](https://github.com/open-telemetry/prometheus-interoperability-spec/issues/38) -- HIGH confidence (official interop spec)
- K8s ConfigMap propagation delay: [Kubernetes ConfigMaps docs](https://kubernetes.io/docs/concepts/configuration/configmap/) -- HIGH confidence (official K8s docs)
- K8s ConfigMap watch delay vs volume mount: [Kubernetes issue #30189](https://github.com/kubernetes/kubernetes/issues/30189) -- MEDIUM confidence (community issue with maintainer responses)
- System source code analysis: `ServiceCollectionExtensions.cs`, `MetricRoleGatedExporter.cs`, `OtelMetricHandler.cs`, `OidMapWatcherService.cs`, `K8sLeaseElection.cs`, `PipelineMetricService.cs`, `SnmpMetricFactory.cs` -- HIGH confidence (direct source verification)

---
*Pitfalls research for: E2E system verification of SNMP monitoring pipeline*
*Researched: 2026-03-09*
