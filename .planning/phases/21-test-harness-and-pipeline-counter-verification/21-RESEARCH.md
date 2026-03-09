# Phase 21: Test Harness and Pipeline Counter Verification - Research

**Researched:** 2026-03-09
**Domain:** Bash E2E test runner, Prometheus HTTP API, SNMP pipeline counter verification
**Confidence:** HIGH

## Summary

This phase builds a bash-based E2E test runner that verifies all 10 pipeline counters increment correctly via Prometheus delta queries. The codebase investigation reveals a critical naming mismatch: the success criteria reference `oid_resolved`, `oid_unresolved`, and `event_validation_failed` counters that do not exist. The actual 10 counters in `PipelineMetricService.cs` are: `snmp.event.published`, `snmp.event.handled`, `snmp.event.errors`, `snmp.event.rejected`, `snmp.poll.executed`, `snmp.trap.received`, `snmp.trap.auth_failed`, `snmp.trap.dropped`, `snmp.poll.unreachable`, and `snmp.poll.recovered`.

The E2E simulator (`simulators/e2e-sim/`) already provides the necessary stimuli for most counters: valid traps (every 30s), bad-community traps (every 45s), and poll-able OIDs (7 mapped + 2 unmapped). The `DeviceWatcherService` watches the `simetra-devices` ConfigMap via K8s API and hot-reloads devices + reconciles poll schedules automatically -- no deployment restart is needed for the fake device unreachable/recovered test.

**Primary recommendation:** Build modular test runner in `tests/e2e/` with `lib/` utilities and per-scenario scripts. Use the existing E2E-SIM simulator for trap/poll counters, a fake device ConfigMap patch for unreachability testing (with K8s watch-based hot-reload), and existence-check assertions for the 3 error sentinel counters that cannot be naturally triggered.

## Standard Stack

### Core
| Tool | Purpose | Why Standard |
|------|---------|--------------|
| bash | Test runner scripting | Available on all Linux/Mac, runs in CI, no compilation needed |
| curl | Prometheus HTTP API queries | Standard HTTP client, parses JSON with jq |
| jq | JSON parsing for Prometheus responses | De facto standard for CLI JSON processing |
| kubectl | K8s interaction (port-forward, logs, apply/delete) | Native K8s CLI |

### Supporting
| Tool | Purpose | When to Use |
|------|---------|-------------|
| date +%s | Timing for delta calculations | Capture before/after timestamps |
| bc | Floating-point arithmetic | Delta threshold comparisons |
| mktemp | Temporary report file creation | Store intermediate evidence |

### Not Needed
| Instead of | Why Not |
|------------|---------|
| Python test frameworks | Overkill for 10 counter checks; bash + curl + jq is simpler |
| BATS (bash testing framework) | Adds dependency; plain bash with pass/fail functions is sufficient |
| Prometheus client libraries | Only need HTTP GET queries, curl is enough |

## Architecture Patterns

### Recommended Project Structure
```
tests/e2e/
├── run-all.sh              # Entry point: pre-flight, port-forwards, scenarios, report
├── lib/
│   ├── common.sh           # Colors, logging, pass/fail tracking
│   ├── prometheus.sh       # query_counter(), poll_until(), snapshot_counter()
│   ├── kubectl.sh          # wait_pods_ready(), port_forward_start(), apply_manifest()
│   └── report.sh           # Markdown report generation
├── scenarios/
│   ├── 01-poll-executed.sh         # poll_executed delta > 0 for known devices
│   ├── 02-event-published.sh      # event_published delta > 0
│   ├── 03-event-handled.sh        # event_handled delta > 0
│   ├── 04-trap-received.sh        # trap_received delta > 0 (E2E-SIM valid traps)
│   ├── 05-trap-auth-failed.sh     # trap_auth_failed delta > 0 (E2E-SIM bad community)
│   ├── 06-poll-unreachable.sh     # Add fake device, wait for unreachable transition
│   ├── 07-poll-recovered.sh       # Patch fake device IP to reachable, verify recovery
│   ├── 08-event-rejected.sh       # event_rejected: existence check (>= 0)
│   ├── 09-event-errors.sh         # event_errors: existence check (>= 0)
│   └── 10-trap-dropped.sh         # trap_dropped: existence check (>= 0)
└── fixtures/
    └── fake-device-configmap.yaml  # Full ConfigMap with fake unreachable device appended
```

### Pattern 1: Delta-Based Counter Assertion
**What:** Snapshot counter value before stimulus, wait for activity, snapshot after, assert delta > threshold.
**When to use:** Every counter verification scenario where activity is expected (7 of 10 counters).
**Example:**
```bash
# Snapshot before
BEFORE=$(query_counter "snmp_poll_executed_total" '{device_name="OBP-01"}')

# Wait for activity (poll interval is 10s, OTel export is 15s)
poll_until 30 3 "snmp_poll_executed_total{device_name=\"OBP-01\"}" "$BEFORE"

# Snapshot after
AFTER=$(query_counter "snmp_poll_executed_total" '{device_name="OBP-01"}')
DELTA=$((AFTER - BEFORE))

assert_delta_gt "$DELTA" 0 "poll_executed increments for OBP-01"
```

### Pattern 2: Poll-Until-Satisfied
**What:** Query Prometheus every INTERVAL seconds until value changes or TIMEOUT expires.
**When to use:** Waiting for counters to reflect activity (OTel 15s export + Prometheus remote write = up to 20s latency).
**Example:**
```bash
# poll_until TIMEOUT INTERVAL METRIC_QUERY BASELINE_VALUE
poll_until() {
    local timeout=$1 interval=$2 query=$3 baseline=$4
    local deadline=$(($(date +%s) + timeout))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        local current
        current=$(query_prometheus "$query")
        if [ "$current" -gt "$baseline" ]; then
            return 0
        fi
        sleep "$interval"
    done
    return 1  # timeout
}
```

### Pattern 3: Port-Forward Lifecycle
**What:** Start port-forwards as background processes, register PIDs for cleanup via trap EXIT.
**When to use:** Test runner startup/teardown.
**Example:**
```bash
PF_PIDS=()

start_port_forward() {
    local svc=$1 local_port=$2 remote_port=$3
    kubectl port-forward "svc/$svc" "$local_port:$remote_port" -n simetra &>/dev/null &
    PF_PIDS+=($!)
    # Wait briefly for port-forward to establish
    sleep 2
}

cleanup() {
    for pid in "${PF_PIDS[@]}"; do
        kill "$pid" 2>/dev/null
    done
}
trap cleanup EXIT
```

### Pattern 4: ConfigMap Hot-Reload for Fake Device
**What:** Modify the `simetra-devices` ConfigMap to add/modify a fake device. The `DeviceWatcherService` watches via K8s API and automatically reloads `IDeviceRegistry` + reconciles `DynamicPollScheduler`.
**When to use:** Unreachable/recovered testing without pod restarts.
**Example:**
```bash
# Add fake device to ConfigMap (kubectl patch or apply full ConfigMap)
kubectl apply -f fixtures/fake-device-configmap.yaml

# DeviceWatcherService picks up change automatically via K8s watch API
# Wait for DeviceWatcher to process (~5s) then wait for poll cycles
sleep 5

# Now wait for 3 poll failures (10s interval x 3 = 30s) + OTel export (15s)
poll_until 60 3 "snmp_poll_unreachable_total{device_name=\"FAKE-UNREACHABLE\"}" "0"
```

### Anti-Patterns to Avoid
- **Fixed sleeps instead of polling:** Never `sleep 30 && check`. Always poll with timeout.
- **Absolute counter assertions:** Never assert `counter == 5`. Always use deltas (post - pre >= threshold).
- **Ignoring device_name labels:** Always filter by `device_name` to exclude heartbeat noise. Heartbeat device name is `__heartbeat__`.
- **Testing all counters in one scenario:** Each counter gets its own scenario for independent pass/fail.
- **Restarting deployment for config changes:** Use ConfigMap apply -- DeviceWatcherService handles hot-reload via K8s watch API.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON parsing | String manipulation / grep | `jq` | Prometheus responses are nested JSON; jq handles edge cases |
| Prometheus queries | Raw HTTP construction | `curl -s -G --data-urlencode` | Proper URL encoding of PromQL |
| Process cleanup | Manual kill calls | `trap EXIT` handler | Catches Ctrl+C, errors, normal exit |
| Floating-point comparison | bash arithmetic | `echo "$a > $b" \| bc` | bash can't do floats natively |
| Device config reload | Deployment restart | ConfigMap apply (K8s watch hot-reload) | DeviceWatcherService handles automatically |

## Common Pitfalls

### Pitfall 1: OTel Export Latency
**What goes wrong:** Counter delta is 0 even though activity occurred.
**Why it happens:** OTel exports every 15s, then remote-writes to Prometheus. Total latency can be up to 20s.
**How to avoid:** Use poll_until with 30s timeout, 3s interval. Never assert immediately after stimulus.
**Warning signs:** Flaky tests that pass sometimes but not always.

### Pitfall 2: Prometheus _total Suffix
**What goes wrong:** Query returns empty result.
**Why it happens:** OTel dotted metric names (`snmp.event.published`) become `snmp_event_published_total` in Prometheus (dots to underscores, `_total` suffix for counters).
**How to avoid:** Always use the Prometheus name format: `snmp_event_published_total`, `snmp_poll_executed_total`, etc.

### Pitfall 3: Cumulative Counter Reset on Pod Restart
**What goes wrong:** Delta is negative (after < before) if pod restarts during test.
**Why it happens:** Counters reset to 0 on restart (cumulative temporality).
**How to avoid:** Pre-flight checks ensure pods are Running and stable. If delta is negative, fail with clear message about pod restart.

### Pitfall 4: Unreachability Threshold (3 Consecutive Failures)
**What goes wrong:** `poll_unreachable` counter never increments after adding fake device.
**Why it happens:** Unreachability transition fires only after 3 consecutive poll failures (hardcoded threshold in `DeviceUnreachabilityTracker`). Also, MetricPollJob uses 80% of interval as SNMP timeout -- with 10s interval, each poll can take up to 8s to timeout.
**How to avoid:** Wait for at least 3 poll cycles with timeout. With 10s interval and 8s timeout, need ~30s for 3 failures + OTel export latency = ~50s total. Use poll_until with 60s timeout.

### Pitfall 5: poll_recovered Requires Prior Unreachable State
**What goes wrong:** Patching the device to a reachable IP doesn't trigger `poll_recovered`.
**Why it happens:** `RecordSuccess` only returns true (and increments counter) if device was previously in unreachable state. The device MUST have been marked unreachable first.
**How to avoid:** Scenarios 06 (unreachable) and 07 (recovered) must run in strict order. Recovery strategy: after verifying unreachable, patch the fake device's IP to point to the E2E-SIM simulator (`e2e-simulator.simetra.svc.cluster.local`), wait for a successful poll, verify recovered counter increments.

### Pitfall 6: trap_dropped Requires Channel Overflow
**What goes wrong:** `trap_dropped` counter stays at 0.
**Why it happens:** The BoundedChannel capacity is 1000. The E2E simulator sends 1 trap every 30s. Overflow requires >1000 traps before the consumer drains them -- practically impossible in normal operation.
**How to avoid:** Accept that `trap_dropped` will show delta=0 in normal conditions. Test should verify the counter EXISTS in Prometheus (metric is being exported, value >= 0).

### Pitfall 7: event_rejected Trigger Strategy
**What goes wrong:** No validation failures occur because all simulator data is well-formed.
**Why it happens:** `IncrementRejected` fires only when OID fails regex validation (`^\d+(\.\d+){1,}$`) or DeviceName is null. The E2E simulator always sends valid OIDs and community strings derive valid device names.
**How to avoid:** Like `trap_dropped`, this is a sentinel counter. Test should verify counter EXISTS (value >= 0). Absence of rejections is actually a healthy state.

### Pitfall 8: event_errors Counter
**What goes wrong:** `event_errors` stays at 0.
**Why it happens:** `IncrementErrors` fires only when an unhandled exception occurs inside the MediatR pipeline (ExceptionBehavior catches it). In normal operation, no exceptions occur.
**How to avoid:** Same as trap_dropped and event_rejected: verify counter EXISTS, accept delta=0 as valid.

### Pitfall 9: Multiple Pod Instances and Counter Summing
**What goes wrong:** Query returns counter from only one pod, missing activity on others.
**Why it happens:** Pipeline counters export from ALL instances. Prometheus may have separate time series per `service_instance_id` / `k8s_pod_name`.
**How to avoid:** Use `sum()` aggregation in queries: `sum(snmp_poll_executed_total{device_name="OBP-01"})`.

### Pitfall 10: DeviceWatcherService Reconnection Delay
**What goes wrong:** ConfigMap update not picked up immediately.
**Why it happens:** K8s watch connection may have dropped and DeviceWatcherService is in its 5s reconnection backoff.
**How to avoid:** After applying ConfigMap, add a small wait (5s) before starting the poll timeout. The DeviceWatcherService uses semaphore-serialized reloads and reconnects automatically.

## Code Examples

### Querying a Counter from Prometheus
```bash
# Source: Prometheus HTTP API docs
query_counter() {
    local metric=$1
    local labels=${2:-""}
    local query="sum(${metric}${labels})"
    local result
    result=$(curl -s -G "http://localhost:9090/api/v1/query" \
        --data-urlencode "query=${query}" | jq -r '.data.result[0].value[1] // "0"')
    echo "${result%.*}"  # truncate to integer
}
```

### Checking Counter Existence (for sentinel counters)
```bash
# Returns 0 if metric exists in Prometheus, 1 if not
counter_exists() {
    local metric=$1
    local count
    count=$(curl -s -G "http://localhost:9090/api/v1/query" \
        --data-urlencode "query=count(${metric})" \
        | jq -r '.data.result[0].value[1] // "0"')
    [ "$count" -gt 0 ]
}
```

### Pre-flight Check
```bash
preflight() {
    echo "=== Pre-flight checks ==="

    # Check pods running
    local ready
    ready=$(kubectl get pods -n simetra -l app=snmp-collector \
        -o jsonpath='{.items[*].status.phase}' | tr ' ' '\n' | grep -c Running)
    if [ "$ready" -eq 0 ]; then
        echo "FAIL: No snmp-collector pods in Running state"
        exit 1
    fi
    echo "PASS: $ready snmp-collector pod(s) Running"

    # Check Prometheus reachable (via port-forward already started)
    if ! curl -sf "http://localhost:9090/-/ready" > /dev/null 2>&1; then
        echo "FAIL: Prometheus not reachable at localhost:9090"
        exit 1
    fi
    echo "PASS: Prometheus reachable"

    # Check pipeline metrics are flowing
    local metric_check
    metric_check=$(curl -s -G "http://localhost:9090/api/v1/query" \
        --data-urlencode 'query=count(snmp_event_published_total)' \
        | jq -r '.data.result[0].value[1] // "0"')
    if [ "$metric_check" -eq 0 ]; then
        echo "FAIL: No snmp_event_published_total metrics found in Prometheus"
        exit 1
    fi
    echo "PASS: Pipeline metrics flowing to Prometheus"
}
```

### Markdown Report Generation
```bash
generate_report() {
    local report_file=$1
    cat > "$report_file" <<HEREDOC
# E2E Pipeline Counter Verification Report

**Date:** $(date -u +"%Y-%m-%dT%H:%M:%SZ")
**Pods:** $(kubectl get pods -n simetra -l app=snmp-collector -o name | wc -l)

## Results

| # | Scenario | Result | Delta | Evidence |
|---|----------|--------|-------|----------|
HEREDOC
    for i in "${!SCENARIO_NAMES[@]}"; do
        echo "| $((i+1)) | ${SCENARIO_NAMES[$i]} | ${SCENARIO_RESULTS[$i]} | ${SCENARIO_DELTAS[$i]} | ${SCENARIO_EVIDENCE[$i]} |" >> "$report_file"
    done
}
```

## The Actual 10 Pipeline Counters

**CRITICAL FINDING:** The success criteria reference counter names (`oid_resolved`, `oid_unresolved`, `event_validation_failed`) that do not exist in the codebase. Here is the authoritative mapping from `PipelineMetricService.cs`:

| # | OTel Instrument Name | Prometheus Name | Where Incremented | Trigger |
|---|---------------------|-----------------|-------------------|---------|
| 1 | `snmp.event.published` | `snmp_event_published_total` | `LoggingBehavior` | Every SnmpOidReceived entering pipeline |
| 2 | `snmp.event.handled` | `snmp_event_handled_total` | `OtelMetricHandler` | Every successfully processed event |
| 3 | `snmp.event.errors` | `snmp_event_errors_total` | `ExceptionBehavior` | Unhandled exceptions in pipeline |
| 4 | `snmp.event.rejected` | `snmp_event_rejected_total` | `ValidationBehavior` | Invalid OID format or null DeviceName |
| 5 | `snmp.poll.executed` | `snmp_poll_executed_total` | `MetricPollJob.Execute` (finally block) | Every completed poll attempt |
| 6 | `snmp.trap.received` | `snmp_trap_received_total` | `ChannelConsumerService` | Every trap varbind consumed from channel |
| 7 | `snmp.trap.auth_failed` | `snmp_trap_auth_failed_total` | `SnmpTrapListenerService.ProcessDatagram` | Community string not matching `Simetra.*` |
| 8 | `snmp.trap.dropped` | `snmp_trap_dropped_total` | `TrapChannel` (BoundedChannel callback) | Channel overflow (capacity=1000) |
| 9 | `snmp.poll.unreachable` | `snmp_poll_unreachable_total` | `MetricPollJob.RecordFailure` | Transition to unreachable (3 failures) |
| 10 | `snmp.poll.recovered` | `snmp_poll_recovered_total` | `MetricPollJob.Execute` | Transition from unreachable to healthy |

### Counter Testability Classification

| Counter | Testability | Strategy | Expected Delta | Assertion |
|---------|-------------|----------|----------------|-----------|
| `event_published` | EASY | Passive -- existing simulators generate constant activity | > 0 | delta > 0 |
| `event_handled` | EASY | Passive -- same as published (minus errors/rejected) | > 0 | delta > 0 |
| `poll_executed` | EASY | Passive -- polls run every 10s for OBP/NPB/E2E-SIM | > 0 | delta > 0 |
| `trap_received` | EASY | Passive -- E2E-SIM sends valid traps every 30s | > 0 | delta > 0 |
| `trap_auth_failed` | EASY | Passive -- E2E-SIM sends bad-community traps every 45s | > 0 | delta > 0 |
| `poll_unreachable` | MODERATE | Active -- add fake device via ConfigMap patch, wait 3+ poll cycles | > 0 | delta > 0 |
| `poll_recovered` | MODERATE | Active -- patch fake device IP to reachable simulator, wait for success | > 0 | delta > 0 |
| `event_errors` | SENTINEL | Passive only -- requires pipeline exception (abnormal condition) | >= 0 | existence |
| `event_rejected` | SENTINEL | Passive only -- requires malformed OID or null DeviceName | >= 0 | existence |
| `trap_dropped` | SENTINEL | Passive only -- requires >1000 buffered traps before consumer drains | >= 0 | existence |

## Device Hot-Reload Architecture (Confirmed)

**RESOLVED (previously open question):** `DeviceWatcherService` watches the `simetra-devices` ConfigMap via the Kubernetes watch API. On `Added` or `Modified` events:

1. Parses `devices.json` from ConfigMap data
2. Calls `_deviceRegistry.ReloadAsync(devices)` to update in-memory device registry
3. Calls `_pollScheduler.ReconcileAsync(devices)` to add/remove/update Quartz poll jobs
4. Uses `SemaphoreSlim` to serialize concurrent reloads

**Implication:** Adding a fake unreachable device only requires `kubectl apply` of the modified ConfigMap. No pod restart needed. The DeviceWatcherService picks up the change within seconds. This preserves all existing counter values and avoids disrupting passive counter tests.

**K8s Watch reconnection:** The watch connection times out after ~30 minutes (K8s server-side default). DeviceWatcherService reconnects automatically with 5s backoff on error.

## Fake Device Strategy for Unreachable/Recovered Testing

### Step-by-Step (No Restart Required)

**Phase A: Test Unreachable**
1. Save original ConfigMap: `kubectl get configmap simetra-devices -n simetra -o yaml > /tmp/original-devices.yaml`
2. Apply modified ConfigMap with fake device appended (IP: `10.255.255.254`, non-routable):
```json
{
  "Name": "FAKE-UNREACHABLE",
  "IpAddress": "10.255.255.254",
  "Port": 161,
  "MetricPolls": [
    {
      "IntervalSeconds": 10,
      "Oids": ["1.3.6.1.2.1.1.1.0"]
    }
  ]
}
```
3. DeviceWatcherService detects change, reloads registry, creates new Quartz poll job
4. Wait ~50-60s: 3 polls x (10s interval + 8s timeout) + OTel export (15s)
5. Verify: `sum(snmp_poll_unreachable_total{device_name="FAKE-UNREACHABLE"})` delta > 0

**Phase B: Test Recovered**
6. Patch ConfigMap: change fake device IP to `e2e-simulator.simetra.svc.cluster.local` and community to `Simetra.E2E-SIM` (so E2E-SIM responds)

   **NOTE:** The community string matters. MetricPollJob derives community from device name: `CommunityStringHelper.DeriveFromDeviceName("FAKE-UNREACHABLE")` = `Simetra.FAKE-UNREACHABLE`. The E2E simulator only accepts `Simetra.E2E-SIM`. Options:
   - Set explicit `CommunityString` on the fake device to `Simetra.E2E-SIM`, OR
   - Use a device name that matches an existing simulator's community

   **Recommended:** Add a `CommunityString` field to the fake device config. Check if `DeviceOptions` supports it.
7. DeviceWatcherService reloads, poll job starts succeeding
8. First successful poll triggers `RecordSuccess` -> unreachable=false -> counter incremented
9. Verify: `sum(snmp_poll_recovered_total{device_name="FAKE-UNREACHABLE"})` delta > 0

**Phase C: Cleanup**
10. Restore original ConfigMap: `kubectl apply -f /tmp/original-devices.yaml`
11. DeviceWatcherService removes fake device's poll job automatically

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Fixed sleep-based waits | poll-until-satisfied with timeout | Project convention | Eliminates flaky tests from timing assumptions |
| grep-based JSON parsing | jq for all Prometheus responses | N/A | Correct nested JSON handling |
| Single monolithic test script | Modular lib/ + scenarios/ | N/A | Each counter independently testable |
| Deployment restart for config | K8s watch-based hot-reload | DeviceWatcherService | No counter disruption during config changes |

## Open Questions

1. **DeviceOptions CommunityString Field**
   - What we know: `MetricPollJob` checks `device.CommunityString` and falls back to `DeriveFromDeviceName(device.Name)`. The field exists in code.
   - What's unclear: Whether `DeviceOptions` (the config DTO) also has a `CommunityString` property that deserializes from JSON.
   - Recommendation: Verify `DeviceOptions` class. If it has `CommunityString`, the fake device can set it to `Simetra.E2E-SIM` for recovery testing. If not, name the fake device something the E2E-SIM recognizes or add the field.

2. **event_errors, event_rejected, trap_dropped -- Acceptance Criteria**
   - What we know: These counters are error/anomaly indicators. Zero is the healthy state.
   - What's unclear: The success criteria says "All 10 pipeline counters show non-zero deltas." Three counters may legitimately be zero.
   - Recommendation: For these three, assert counter EXISTS in Prometheus (metric is being exported) rather than asserting delta > 0. Document this as intentional -- these are sentinel counters that indicate healthy pipeline when at zero.

3. **Prometheus Remote Write vs Scrape Path**
   - What we know: OTel Collector uses `prometheusremotewrite` exporter (push to `/api/v1/write`). The Prometheus scrape of `otel-collector:8889` is noted as a "dead target" in config comments.
   - What's unclear: Whether counters appear via remote write with identical metric names/labels.
   - Recommendation: Should work -- `_total` suffix is standard for counters in Prometheus format. Verify during pre-flight by checking any `snmp_*` metric exists. This has already been confirmed working by the Grafana dashboards.

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` -- All 10 counter definitions with exact OTel instrument names
- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` -- event_rejected trigger conditions
- `src/SnmpCollector/Services/SnmpTrapListenerService.cs` -- trap_auth_failed trigger (community string mismatch)
- `src/SnmpCollector/Pipeline/TrapChannel.cs` -- trap_dropped trigger (BoundedChannel overflow, capacity=1000)
- `src/SnmpCollector/Jobs/MetricPollJob.cs` -- poll_executed, poll_unreachable, poll_recovered logic
- `src/SnmpCollector/Pipeline/DeviceUnreachabilityTracker.cs` -- 3 consecutive failures threshold
- `src/SnmpCollector/Services/DeviceWatcherService.cs` -- K8s watch-based ConfigMap hot-reload (confirmed)
- `simulators/e2e-sim/e2e_simulator.py` -- E2E-SIM behavior: valid traps (30s), bad-community traps (45s), 9 OIDs
- `deploy/k8s/snmp-collector/simetra-devices.yaml` -- Device config with E2E-SIM entry
- `deploy/grafana/dashboards/simetra-operations.json` -- Confirmed Prometheus metric names with `_total` suffix

### Secondary (MEDIUM confidence)
- Prometheus HTTP API query format (well-documented, stable API)
- jq JSON processing patterns (de facto standard)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- bash/curl/jq/kubectl are established tools, no library research needed
- Architecture: HIGH -- patterns derived directly from codebase analysis of counter mechanics
- Counter mapping: HIGH -- read directly from source code, cross-verified with Grafana dashboard
- Pitfalls: HIGH -- derived from code analysis (thresholds, channel capacity, export intervals)
- Fake device strategy: HIGH -- DeviceWatcherService hot-reload confirmed via codebase reading

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable -- bash patterns and Prometheus API don't change)
