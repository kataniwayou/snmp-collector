# Architecture: E2E System Verification

**Domain:** E2E test infrastructure for K8s-based SNMP monitoring pipeline
**Researched:** 2026-03-09
**Confidence:** HIGH (based on direct codebase analysis of all integration surfaces)

## Current System Architecture (Baseline)

The system being tested:

```
                        +-----------------+
                        | simetra-oidmaps |  (ConfigMap, K8s API watch)
                        | simetra-devices |  (ConfigMap, K8s API watch)
                        +--------+--------+
                                 |
                          K8s API watch (sub-second)
                                 |
+---------------+    UDP    +----v-----------+    OTLP/gRPC    +---------------+   promremotewrite   +------------+
| OBP Simulator |---------->|                |---------------->|               |-------------------->|            |
| (port 161)    |   traps   | snmp-collector |                 | OTel Collector|                     | Prometheus |
+---------------+   +poll   | (3 replicas)   |                 |               |                     | :9090      |
                    |       |                |                 +---------------+                     +------------+
+---------------+   |       | MediatR pipe:  |
| NPB Simulator |---+       | Log->Exc->Val  |
| (port 161)    |           | ->OidRes->Otel |
+---------------+           +----------------+
                               |
                        Quartz poll jobs
                        (10s interval)
```

**Integration surfaces the E2E tests must exercise:**

1. **SNMP UDP port 10162** on snmp-collector pods -- traps arrive here via headless service `simetra-pods`
2. **SNMP UDP port 161** on simulator services -- polls originate from snmp-collector MetricPollJob
3. **ConfigMaps** `simetra-oidmaps` and `simetra-devices` -- K8s API watch triggers reload in OidMapWatcherService / DeviceWatcherService
4. **Prometheus HTTP API** at `prometheus:9090` -- query endpoint for metric verification
5. **Pod logs** via `kubectl logs` -- structured JSON logs for evidence collection

## Recommended E2E Test Architecture

### Overview

```
+------------------+       kubectl apply/patch        +------------------+
|                  |----(ConfigMap manipulation)------>| K8s API Server   |
|   Test Runner    |                                  +------------------+
|   (bash script)  |
|                  |       Prometheus HTTP API         +------------------+
|   Runs on host   |----(query for verification)----->| Prometheus :9090 |
|   outside K8s    |       via port-forward            +------------------+
|                  |
|                  |       kubectl logs                +------------------+
|                  |----(log evidence collection)----->| snmp-collector   |
|                  |                                  | pods (x3)        |
|                  |                                  +------------------+
|                  |
|                  |       kubectl apply/delete        +------------------+
|                  |----(deploy/teardown)------------->| Test Simulator   |
|                  |                                  | (e2e-sim, K8s)   |
+------------------+                                  +------------------+
```

**Key design decision:** The test runner lives on the host machine (not in-cluster). This matches the existing `deploy/k8s/verify-e2e.sh` pattern and avoids the complexity of in-cluster RBAC, ServiceAccounts, and log collection from inside the cluster.

### Component Inventory

| Component | Status | Location | Language |
|-----------|--------|----------|----------|
| Test Runner | **NEW** | `tests/e2e/run-e2e.sh` | Bash |
| Test Simulator | **NEW** | `simulators/e2e/e2e_simulator.py` | Python |
| Test Simulator Dockerfile | **NEW** | `simulators/e2e/Dockerfile` | Dockerfile |
| Test Simulator K8s Manifest | **NEW** | `deploy/k8s/simulators/e2e-simulator.yaml` | YAML |
| Test ConfigMap Fixtures | **NEW** | `tests/e2e/fixtures/` | JSON |
| snmp-collector | **UNMODIFIED** | `src/SnmpCollector/` | C# |
| OBP Simulator | **UNMODIFIED** | `simulators/obp/` | Python |
| NPB Simulator | **UNMODIFIED** | `simulators/npb/` | Python |
| Prometheus | **UNMODIFIED** | `deploy/k8s/monitoring/` | -- |
| OTel Collector | **UNMODIFIED** | `deploy/k8s/monitoring/` | -- |

### Design Principle: No SnmpCollector Modifications

Per v1.4 requirements, this is a verification-only milestone. The test infrastructure is purely additive -- it deploys alongside the existing system, exercises it through its existing interfaces (SNMP UDP, ConfigMap API, Prometheus HTTP), and reports findings. Zero C# code changes.

---

## Component Details

### 1. Test Simulator (`e2e-sim`)

**Purpose:** Provide controllable SNMP responses for edge cases that OBP/NPB simulators cannot cover.

**Why a new simulator instead of extending OBP/NPB:**
- OBP/NPB simulators represent real device behavior and must stay stable
- E2E tests need deterministic, weird, and broken scenarios (unknown OIDs, specific trap sequences)
- Test simulator can be deployed/torn down per scenario without disrupting other simulators
- Community string `Simetra.E2E-SIM` avoids collision with existing devices

**Architecture:** Same pattern as OBP/NPB simulators -- pysnmp 7.1.22, asyncio event loop, DynamicInstance for mutable values, `supervised_task` for crash recovery.

**OID Space:**

Uses enterprise subtree `47477.999` to clearly separate from real device OIDs (`47477.10.21` for OBP, `47477.100` for NPB).

| OID | Metric Name in oidmaps | SNMP Type | Purpose |
|-----|------------------------|-----------|---------|
| `1.3.6.1.4.1.47477.999.1.1.0` | `e2e_gauge_int` | Integer32 | Basic gauge, rename/remove test target |
| `1.3.6.1.4.1.47477.999.1.2.0` | `e2e_gauge_g32` | Gauge32 | Gauge32 type verification |
| `1.3.6.1.4.1.47477.999.1.3.0` | `e2e_counter32` | Counter32 | Counter32 recorded as gauge |
| `1.3.6.1.4.1.47477.999.1.4.0` | `e2e_counter64` | Counter64 | Counter64 recorded as gauge |
| `1.3.6.1.4.1.47477.999.1.5.0` | `e2e_timeticks` | TimeTicks | TimeTicks type verification |
| `1.3.6.1.4.1.47477.999.1.6.0` | `e2e_info_str` | OctetString | snmp_info instrument verification |
| `1.3.6.1.4.1.47477.999.1.7.0` | `e2e_info_ip` | IpAddress | IpAddress -> snmp_info verification |
| `1.3.6.1.4.1.47477.999.1.8.0` | `e2e_info_oid` | ObjectIdentifier | OID type -> snmp_info verification |
| `1.3.6.1.4.1.47477.999.2.1.0` | *(unmapped)* | Integer32 | Unknown OID -> metric_name="Unknown" |
| `1.3.6.1.4.1.47477.999.2.2.0` | *(unmapped)* | OctetString | Unknown OID info type handling |

**Trap OIDs:**

| Trap OID | Varbind OID | Varbind Type | Purpose |
|----------|-------------|--------------|---------|
| `1.3.6.1.4.1.47477.999.3.1` | `.999.1.1.0` | Integer32 | Basic trap -> snmp_gauge with source=trap |
| `1.3.6.1.4.1.47477.999.3.2` | `.999.2.1.0` | Integer32 | Trap with unmapped varbind OID -> Unknown |

**Community string:** `Simetra.E2E-SIM` -- follows the existing `Simetra.{DeviceName}` convention that `CommunityStringHelper.TryExtractDeviceName` expects.

**Trap timing:** Deterministic 10-second interval (not random 60-300s like OBP/NPB). This keeps E2E test run time manageable. Both trap OIDs fire alternately every 10s.

**Environment variables (same pattern as OBP/NPB):**
- `DEVICE_NAME=E2E-SIM`
- `COMMUNITY=Simetra.E2E-SIM`
- `TRAP_TARGET=simetra-pods.simetra.svc.cluster.local`
- `TRAP_PORT=10162`
- `TRAP_INTERVAL=10` (deterministic, not min/max range)

**Dockerfile:** Identical to `simulators/obp/Dockerfile` -- same base image, same `requirements.txt` (pysnmp==7.1.22).

**Health probe:** Same pysnmp GET self-check pattern as OBP/NPB, querying `.999.1.1.0` with `Simetra.E2E-SIM` community.

### 2. Test Runner (`run-e2e.sh`)

**Purpose:** Orchestrate test scenarios sequentially, verify outcomes via Prometheus and kubectl, generate a report.

**Location:** `tests/e2e/run-e2e.sh`

**Architecture:** Extends the existing `deploy/k8s/verify-e2e.sh` pattern. That script already demonstrates the correct approach: port-forward Prometheus, query the HTTP API, pass/fail with counts. The E2E runner adds:

1. **Scenario sequencing** -- scenarios run one at a time in a defined order
2. **ConfigMap manipulation** -- `kubectl get -> jq merge -> kubectl apply` for safe patching
3. **ConfigMap snapshotting** -- save originals at start, restore on exit (even on failure)
4. **Wait-for-condition** -- poll Prometheus with timeout (existing `wait_for_metric` pattern)
5. **Log evidence** -- `kubectl logs --since=Xs` to capture relevant log lines per scenario
6. **Cleanup** -- bash `trap EXIT` handler guarantees ConfigMap restore and test simulator teardown

**Dependencies:** `curl`, `jq`, `kubectl` -- same as existing verify script, no new tools.

**Execution model:**

```
run-e2e.sh
  |
  +-- Pre-flight: cluster reachable, pods healthy, Prometheus accessible
  |
  +-- Snapshot: save simetra-oidmaps and simetra-devices ConfigMaps
  |
  +-- Phase 1: Pipeline counter verification
  |     Uses existing OBP/NPB (no test simulator needed)
  |     Queries all 10 pipeline counter metrics
  |
  +-- Phase 2: Deploy test simulator + ConfigMap integration
  |     kubectl apply e2e-simulator.yaml
  |     Merge E2E-SIM entries into simetra-oidmaps and simetra-devices
  |     Wait for poll metrics to appear in Prometheus
  |
  +-- Phase 3: Business metric mutations
  |     Rename OID mapping -> verify new metric_name in Prometheus
  |     Remove OID mapping -> verify metric_name="Unknown"
  |     Restore original mapping -> verify original name returns
  |
  +-- Phase 4: Unknown OID verification
  |     .999.2.1.0 and .999.2.2.0 are deliberately unmapped
  |     Verify metric_name="Unknown" in Prometheus
  |
  +-- Phase 5: Device lifecycle
  |     Remove E2E-SIM from devices -> verify polling stops
  |     Re-add E2E-SIM -> verify polling resumes
  |
  +-- Phase 6: ConfigMap watcher resilience
  |     Patch oidmaps with invalid JSON -> verify log warning, old map retained
  |     Restore valid JSON -> verify reload succeeds
  |
  +-- Phase 7: Trap verification
  |     Wait for trap from E2E-SIM in Prometheus (source="trap")
  |     Verify unknown trap varbind OID classified as "Unknown"
  |
  +-- Phase 8: Cleanup + Report
  |     Remove E2E-SIM from ConfigMaps
  |     kubectl delete e2e-simulator
  |     Restore ConfigMaps from snapshots
  |     Generate summary report
```

### 3. ConfigMap Fixture Files

**Location:** `tests/e2e/fixtures/`

| File | Content | Used By |
|------|---------|---------|
| `e2e-oidmaps.json` | 8 OID map entries for E2E-SIM's mapped `.999.1.*` OIDs | Phase 2, 3 |
| `e2e-device.json` | Device entry for E2E-SIM with service DNS, community, poll OIDs | Phase 2, 5 |

**ConfigMap patch strategy:** The runner does NOT use `kubectl patch` directly (which can corrupt JSON-within-YAML). Instead:
1. `kubectl get configmap simetra-oidmaps -o jsonpath='{.data.oidmaps\.json}'` to get current JSON
2. `jq '. + input' current.json fixture.json` to merge
3. `kubectl create configmap simetra-oidmaps --from-file=oidmaps.json=merged.json --dry-run=client -o yaml | kubectl apply -f -`

This preserves all existing OBP/NPB entries while adding/modifying E2E-SIM entries.

### 4. Report Output

**Format:** Plain text to stdout, same style as existing `verify-e2e.sh`:

```
============================================================
 Simetra E2E Verification Report
 Date: 2026-03-09T14:30:00Z
============================================================

[Phase 1] Pipeline Counters
  PASS  snmp_event_published_total (OBP-01)       3 series
  PASS  snmp_event_handled_total (OBP-01)         3 series
  PASS  snmp_poll_executed_total (OBP-01)          3 series
  ...

[Phase 3] Business Metric Mutations
  PASS  OID rename: e2e_gauge_int -> e2e_gauge_renamed    1 series
  PASS  OID remove: metric_name="Unknown"                 1 series
  ...

Summary: 28/28 PASS, 0 FAIL
RESULT: PASS
```

---

## Integration Points (Detailed)

### Integration Point 1: SNMP UDP (Test Simulator <-> snmp-collector)

**Direction:** Bidirectional

- **Poll path:** snmp-collector's `MetricPollJob` sends SNMP GET to `e2e-simulator.simetra.svc.cluster.local:161` (resolved by `DeviceRegistry` DNS resolution at startup/reload). Poll interval configured in `devices.json` (10s, matching existing OBP/NPB).
- **Trap path:** e2e-sim sends SNMPv2c traps to `simetra-pods.simetra.svc.cluster.local:10162` (headless service selecting all snmp-collector pods). All 3 replicas receive the trap; only the leader exports the resulting business metric.

**No code changes.** The existing `DynamicPollScheduler` reconciles Quartz jobs when `DeviceWatcherService` detects a ConfigMap change. Adding E2E-SIM to `simetra-devices` automatically creates a new poll job.

### Integration Point 2: ConfigMap API (Test Runner -> K8s API -> snmp-collector)

**Direction:** Test runner patches ConfigMaps; K8s API watch notifies snmp-collector pods.

**ConfigMaps manipulated:**
- `simetra-oidmaps` (key: `oidmaps.json`) -- Add/rename/remove e2e_* entries. Watched by `OidMapWatcherService`.
- `simetra-devices` (key: `devices.json`) -- Add/remove E2E-SIM device. Watched by `DeviceWatcherService` which triggers `DynamicPollScheduler.ReconcileAsync`.

**Watch behavior verified from source:**
- `OidMapWatcherService` handles `Added` and `Modified` events, skips `Deleted` (retains current map)
- Invalid JSON is caught by `JsonSerializer.Deserialize`, logged as error, skipped (old map retained)
- `SemaphoreSlim` serializes concurrent reload requests
- Watch auto-reconnects every ~30 minutes (K8s server-side timeout)

**Critical safety requirement:** Tests MUST snapshot ConfigMaps before any mutation and restore them in a `trap EXIT` handler. A failed test leaving corrupted ConfigMaps breaks the entire system.

### Integration Point 3: Prometheus HTTP API (Test Runner -> Prometheus)

**Direction:** Test runner queries Prometheus for metric verification.

**Access:** `kubectl port-forward svc/prometheus 9090:9090 -n simetra` (same as existing verify script).

**Key queries:**

| Verification | PromQL Query |
|-------------|-------------|
| All 10 pipeline counters exist | `snmp_event_published_total{job="snmp-collector"}` (and 9 others) |
| E2E-SIM polled gauge metrics | `snmp_gauge{device_name="E2E-SIM",source="poll"}` |
| E2E-SIM polled info metrics | `snmp_info{device_name="E2E-SIM",source="poll"}` |
| E2E-SIM trap metrics | `snmp_gauge{device_name="E2E-SIM",source="trap"}` |
| Unknown OID from poll | `snmp_gauge{device_name="E2E-SIM",metric_name="Unknown",source="poll"}` |
| Unknown OID from trap | `snmp_gauge{device_name="E2E-SIM",metric_name="Unknown",source="trap"}` |
| Renamed metric appears | `snmp_gauge{device_name="E2E-SIM",metric_name="e2e_gauge_renamed"}` |
| Poll counter incrementing | `increase(snmp_poll_executed_total{device_name="E2E-SIM"}[1m]) > 0` |
| Poll counter stopped | `increase(snmp_poll_executed_total{device_name="E2E-SIM"}[30s]) == 0` |

**Prometheus metric name mapping (from PipelineMetricService):**

| OTel Instrument | Prometheus Name | Labels |
|-----------------|-----------------|--------|
| `snmp.event.published` | `snmp_event_published_total` | `device_name` |
| `snmp.event.handled` | `snmp_event_handled_total` | `device_name` |
| `snmp.event.errors` | `snmp_event_errors_total` | `device_name` |
| `snmp.event.rejected` | `snmp_event_rejected_total` | `device_name` |
| `snmp.poll.executed` | `snmp_poll_executed_total` | `device_name` |
| `snmp.trap.received` | `snmp_trap_received_total` | `device_name` |
| `snmp.trap.auth_failed` | `snmp_trap_auth_failed_total` | `device_name` |
| `snmp.trap.dropped` | `snmp_trap_dropped_total` | `device_name` |
| `snmp.poll.unreachable` | `snmp_poll_unreachable_total` | `device_name` |
| `snmp.poll.recovered` | `snmp_poll_recovered_total` | `device_name` |

Business metrics (leader-only export via `SnmpCollector.Leader` meter):
- `snmp_gauge` -- labels: `metric_name`, `oid`, `device_name`, `ip`, `source`, `snmp_type`
- `snmp_info` -- labels: `metric_name`, `oid`, `device_name`, `ip`, `source`, `snmp_type`, `value`

### Integration Point 4: Pod Logs (Test Runner -> kubectl)

**Direction:** Test runner reads pod logs for evidence.

**Log patterns to verify (from source code analysis):**

| Scenario | Expected Log Pattern | Source Class |
|----------|---------------------|--------------|
| OID map reload | `"OID map reload complete: {OidCount} entries"` | OidMapWatcherService |
| Watch event received | `"OidMapWatcher received {EventType} event"` | OidMapWatcherService |
| Invalid JSON rejected | `"Failed to parse oidmaps.json from ConfigMap"` | OidMapWatcherService |
| Missing key in ConfigMap | `"does not contain key oidmaps.json"` | OidMapWatcherService |
| Device config reload | Device watcher equivalent logs | DeviceWatcherService |
| Trap auth failure | `"Trap dropped: invalid community string"` | SnmpTrapListenerService |

**Collection method:** `kubectl logs -n simetra -l app=snmp-collector --since=30s --all-containers` captures all 3 replicas. Pipe through `grep` for specific patterns.

---

## K8s Manifest Design: Test Simulator

The manifest follows the exact same pattern as `deploy/k8s/simulators/obp-deployment.yaml`:

```yaml
# deploy/k8s/simulators/e2e-simulator.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: e2e-simulator
  namespace: simetra
  labels:
    app: e2e-simulator
    purpose: testing
spec:
  replicas: 1
  selector:
    matchLabels:
      app: e2e-simulator
  template:
    metadata:
      labels:
        app: e2e-simulator
    spec:
      containers:
      - name: e2e-simulator
        image: e2e-simulator:local
        imagePullPolicy: Never
        ports:
        - containerPort: 161
          name: snmp
          protocol: UDP
        env:
        - name: DEVICE_NAME
          value: "E2E-SIM"
        - name: TRAP_TARGET
          value: "simetra-pods.simetra.svc.cluster.local"
        - name: TRAP_PORT
          value: "10162"
        - name: TRAP_INTERVAL
          value: "10"
        resources:
          requests:
            cpu: 50m
            memory: 64Mi
          limits:
            cpu: 100m
            memory: 128Mi
        livenessProbe:
          exec:
            command:
            - python
            - -c
            - |
              import sys
              from pysnmp.hlapi.v3arch.asyncio import *
              import asyncio
              async def check():
                  engine = SnmpEngine()
                  err, _, _, _ = await get_cmd(
                      engine,
                      CommunityData('Simetra.E2E-SIM'),
                      await UdpTransportTarget.create(('127.0.0.1', 161), timeout=3, retries=0),
                      ContextData(),
                      ObjectType(ObjectIdentity('1.3.6.1.4.1.47477.999.1.1.0'))
                  )
                  engine.close_dispatcher()
                  sys.exit(1 if err else 0)
              asyncio.run(check())
          initialDelaySeconds: 10
          periodSeconds: 30
          timeoutSeconds: 10
---
apiVersion: v1
kind: Service
metadata:
  name: e2e-simulator
  namespace: simetra
  labels:
    app: e2e-simulator
spec:
  selector:
    app: e2e-simulator
  ports:
  - name: snmp
    port: 161
    targetPort: snmp
    protocol: UDP
```

---

## Data Flow: Example Scenario

**OID map rename test** (Phase 3, Scenario A):

```
1. Test runner snapshots simetra-oidmaps ConfigMap

2. Test runner merges fixture into oidmaps.json:
   Adds: "1.3.6.1.4.1.47477.999.1.1.0": "e2e_gauge_renamed"
   (was: "e2e_gauge_int")

3. kubectl apply triggers K8s API MODIFIED event
   -> OidMapWatcherService receives event on all 3 pods
   -> HandleConfigMapChangedAsync deserializes new JSON
   -> OidMapService.UpdateMap replaces dictionary
   -> Log: "OID map reload complete: N entries"

4. Next MetricPollJob cycle (within 10s):
   -> SNMP GET to e2e-simulator for .999.1.1.0
   -> OidResolutionBehavior looks up in new map
   -> Resolves to "e2e_gauge_renamed"
   -> OtelMetricHandler records snmp_gauge{metric_name="e2e_gauge_renamed",...}

5. OTel export (within 10s) -> OTel Collector -> prometheusremotewrite -> Prometheus

6. Test runner polls Prometheus (30s timeout, 5s interval):
   Query: snmp_gauge{device_name="E2E-SIM",metric_name="e2e_gauge_renamed"}
   -> PASS if result count > 0

7. Test runner restores original ConfigMap from snapshot
```

---

## Timing Budgets

| Event Chain | Expected Latency | Test Budget |
|-------------|-----------------|-------------|
| ConfigMap patch -> watch event delivery | <1s | -- |
| Watch event -> OID map/device reload | <1s | -- |
| Next poll cycle after reload | 0-10s (poll interval) | -- |
| OTel periodic export | up to 10s (SDK default) | -- |
| Prometheus scrape interval | 5s (configured in prometheus.yml) | -- |
| **Total: ConfigMap change -> queryable** | **~25s worst case** | **30s timeout** |
| Trap fired -> queryable in Prometheus | ~15s | **30s timeout** |
| Test simulator deploy -> ready | ~15-20s (local image, no pull) | **60s timeout** |
| Full E2E suite runtime estimate | -- | **~10-15 minutes** |

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: In-Cluster Test Runner
**What:** Running the test orchestrator as a K8s Job or Pod.
**Why bad:** Requires ServiceAccount with ConfigMap read/write RBAC, complicates log collection (kubectl from inside pod), adds deployment complexity. The test needs `kubectl` access anyway for ConfigMap manipulation.
**Instead:** Run on host machine where `kubectl` is configured. Matches existing `verify-e2e.sh` pattern.

### Anti-Pattern 2: Modifying Existing Simulators
**What:** Adding test modes or flags to OBP/NPB simulators.
**Why bad:** OBP/NPB represent real device behavior. Mixing test concerns pollutes their purpose. If a test-mode bug introduces wrong OIDs, it masks real pipeline issues.
**Instead:** Dedicated test simulator with its own OID space under `.999`.

### Anti-Pattern 3: Parallel Scenario Execution
**What:** Running ConfigMap mutation scenarios concurrently for speed.
**Why bad:** ConfigMap patches are global. Two scenarios patching `simetra-oidmaps` simultaneously race. Leader election means only one pod exports business metrics, so parallel verification queries could see partial/inconsistent results.
**Instead:** Sequential scenarios with clear setup -> wait -> verify -> teardown per scenario.

### Anti-Pattern 4: Fixed Sleep Instead of Polling
**What:** `sleep 30 && curl prometheus` instead of polling with timeout.
**Why bad:** Pipeline latency varies (OTel export interval, Prometheus scrape timing). Fixed sleeps either waste time (too long) or cause false failures (too short).
**Instead:** Poll with timeout using existing `wait_for_metric` pattern from `verify-e2e.sh`. Budget 30s for ConfigMap-triggered changes, 60s for deployment readiness.

### Anti-Pattern 5: Not Snapshotting ConfigMaps Before Mutation
**What:** Patching ConfigMaps without saving original state.
**Why bad:** If a test fails mid-scenario, the ConfigMap is left corrupted. All subsequent tests fail. Production simulators (OBP/NPB) stop getting their OIDs resolved. The system is broken until manual intervention.
**Instead:** Snapshot all ConfigMaps at test start. Restore from snapshot in a bash `trap EXIT` handler, guaranteeing cleanup on success, failure, or interruption.

### Anti-Pattern 6: Using `kubectl patch` for JSON-in-ConfigMap
**What:** `kubectl patch configmap simetra-oidmaps --type=merge -p '...'` to modify `oidmaps.json`.
**Why bad:** The `oidmaps.json` value is a JSON string stored inside a YAML `.data` field. `kubectl patch` operates on the YAML structure, not the inner JSON. Escaping is fragile and error-prone.
**Instead:** Extract -> jq merge -> recreate:
```bash
kubectl get cm simetra-oidmaps -n simetra -o jsonpath='{.data.oidmaps\.json}' > /tmp/current.json
jq -s '.[0] * .[1]' /tmp/current.json fixture.json > /tmp/merged.json
kubectl create cm simetra-oidmaps -n simetra --from-file=oidmaps.json=/tmp/merged.json --dry-run=client -o yaml | kubectl apply -f -
```

---

## Suggested Build Order

Build order follows dependency chains. Each step can be independently validated before moving on.

### Step 1: Test Simulator

**Build:** `simulators/e2e/e2e_simulator.py`, `simulators/e2e/Dockerfile`, `simulators/e2e/requirements.txt`, `deploy/k8s/simulators/e2e-simulator.yaml`

**Why first:** Everything else depends on having a controllable SNMP device. The simulator can be validated standalone before integration.

**Validation:** Deploy to K8s, exec into a pod, `snmpget -v2c -c Simetra.E2E-SIM e2e-simulator:161 1.3.6.1.4.1.47477.999.1.1.0` returns a value.

### Step 2: Test Runner Framework + Pipeline Counter Tests (Phase 1)

**Build:** `tests/e2e/run-e2e.sh` with pre-flight checks and Phase 1 (pipeline counters for existing OBP/NPB)

**Why second:** Pipeline counter verification uses only existing simulators -- no test simulator needed. This establishes the test framework (port-forward, query helpers, report format) that all subsequent phases reuse.

**Validation:** Run script, verify all 10 pipeline counters reported for OBP-01 and NPB-01.

### Step 3: ConfigMap Fixtures + Test Simulator Integration (Phase 2)

**Build:** `tests/e2e/fixtures/`, ConfigMap snapshot/restore/merge logic, Phase 2 (deploy test simulator, add to ConfigMaps, verify poll metrics appear)

**Why third:** First phase that exercises ConfigMap manipulation and DynamicPollScheduler. The snapshot/restore pattern built here is reused by all subsequent mutation tests.

**Validation:** Run Phases 1-2, verify E2E-SIM gauge and info metrics appear in Prometheus.

### Step 4: Business Metric Mutation + Unknown OID Tests (Phases 3-4)

**Build:** Phases 3-4 (rename, remove/unknown OID verification)

**Why fourth:** Depends on Step 3's ConfigMap merge infrastructure being proven stable.

**Validation:** Run Phases 1-4, verify metric_name changes are visible in Prometheus.

### Step 5: Device Lifecycle + Watcher Resilience Tests (Phases 5-6)

**Build:** Phases 5-6 (device add/remove, invalid JSON resilience)

**Why fifth:** Device lifecycle tests are the most operationally sensitive -- removing a device from ConfigMap affects poll scheduling across all 3 replicas. Build after mutation tests are proven stable.

**Validation:** Run Phases 1-6, verify poll start/stop and invalid JSON handling.

### Step 6: Trap Tests + Cleanup + Final Report (Phases 7-8)

**Build:** Phases 7-8 (trap verification, cleanup, comprehensive report)

**Why last:** Trap tests depend on the simulator being deployed with deterministic trap interval. Report generation wraps everything up.

**Validation:** Full E2E run producing complete pass/fail report.

---

## Sources

All findings are HIGH confidence, derived from direct codebase analysis:

- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` -- all 10 pipeline counter instrument names and labels
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` -- meter name separation (`SnmpCollector` vs `SnmpCollector.Leader`)
- `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` -- leader-only business metric export behavior
- `src/SnmpCollector/Services/OidMapWatcherService.cs` -- ConfigMap watch pattern, error handling, reload serialization
- `src/SnmpCollector/Services/SnmpTrapListenerService.cs` -- community string validation, trap processing
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` -- all SNMP type -> instrument mapping
- `simulators/obp/obp_simulator.py` -- simulator architecture pattern (DynamicInstance, supervised_task, trap loops)
- `deploy/k8s/simulators/obp-deployment.yaml` -- K8s manifest pattern (Deployment + Service + health probe)
- `deploy/k8s/simulators/simetra-headless.yaml` -- headless service for trap delivery to all pods
- `deploy/k8s/production/deployment.yaml` -- snmp-collector deployment config (ports, volumes, probes)
- `deploy/k8s/monitoring/prometheus-configmap.yaml` -- scrape interval (5s)
- `deploy/k8s/verify-e2e.sh` -- existing test pattern (port-forward, check_metric, wait_for_metric, report)

---
*Architecture research for: E2E system verification of SNMP monitoring pipeline*
*Researched: 2026-03-09*
