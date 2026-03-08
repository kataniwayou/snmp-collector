# Phase 16: Test K8s ConfigMap Watchers - Research

**Researched:** 2026-03-08
**Domain:** Live K8s integration testing -- kubectl + pod logs + Prometheus verification
**Confidence:** HIGH

## Summary

This phase creates a manual/scripted UAT plan for verifying OidMapWatcherService and DeviceWatcherService behavior against a running K8s cluster. The testing involves modifying ConfigMaps via kubectl, then verifying changes took effect through pod logs and Prometheus queries.

The codebase already has an established pattern for this: `deploy/k8s/verify-e2e.sh` uses `curl` against the Prometheus HTTP API (`/api/v1/query`) with `jq` to check metric presence. Pod logs are checked via `kubectl logs`. All scenarios are live cluster operations -- no mocks, no testcontainers.

**Primary recommendation:** Structure the UAT as a checklist of kubectl-apply/edit -> wait -> verify-logs -> verify-Prometheus steps, following the exact verify-e2e.sh pattern for Prometheus queries and using the log message strings already present in the watcher source code.

## Standard Stack

This is a manual/scripted test phase. No new libraries needed.

### Core Tools
| Tool | Purpose | Why Standard |
|------|---------|--------------|
| kubectl | Apply/edit/delete ConfigMaps, read pod logs | Standard K8s CLI, already used in project |
| curl + jq | Query Prometheus HTTP API | Already established in verify-e2e.sh |
| Prometheus API | `/api/v1/query` instant query endpoint | Already running in cluster at prometheus:9090 |
| kubectl port-forward | Access Prometheus from local machine | Already used in verify-e2e.sh |

### No New Dependencies
This phase produces a UAT plan document (checklist), not code. No packages to install.

## Architecture Patterns

### ConfigMap Watch Propagation Flow

```
kubectl apply (ConfigMap change)
  -> K8s API server sends watch event (Added/Modified/Deleted)
  -> OidMapWatcherService or DeviceWatcherService receives event
  -> Handler parses JSON, applies changes
  -> Log messages emitted (structured logging)
  -> Next poll cycle uses updated config
  -> OTel exports metrics -> Prometheus remote write
  -> Prometheus query shows changed metrics
```

**Important timing:** The projected volume mount in the deployment does NOT matter for watch-based reload. The watchers use the K8s watch API directly (`ListNamespacedConfigMapWithHttpMessagesAsync` with `watch: true`), not filesystem watching. ConfigMap changes propagate via API server events, typically within 1-2 seconds.

### Verification Pattern (from verify-e2e.sh)

```bash
# Prometheus instant query pattern
curl -s -G "${PROMETHEUS_URL}/api/v1/query" \
  --data-urlencode "query=snmp_gauge{metric_name=\"obp_r1_power_L1\"}" | \
  jq '.data.result | length'

# Pod log check pattern
kubectl logs -n simetra -l app=snmp-collector --tail=50 | grep "OidMap hot-reloaded"
```

### Log Messages to Verify (exact strings from source code)

**OidMapWatcherService success path:**
- `"OidMapWatcher received {EventType} event for {ConfigMap}"` -- watch event arrived
- `"OID map reload complete: {OidCount} entries"` -- UpdateMap succeeded

**OidMapService (UpdateMap) detail logs:**
- `"OidMap hot-reloaded: {EntryCount} entries total, +{Added} added, -{Removed} removed, ~{Changed} changed"` -- diff summary
- `"OidMap added: {Oid} -> {MetricName}"` -- per-entry additions
- `"OidMap removed: {Oid} (was {MetricName})"` -- per-entry removals
- `"OidMap changed: {Oid} {OldName} -> {NewName}"` -- per-entry renames

**OidMapWatcherService error path:**
- `"Failed to parse {ConfigKey} from ConfigMap {ConfigMap} -- skipping reload"` -- malformed JSON
- `"ConfigMap {ConfigMap} was deleted -- skipping reload, retaining current OID map"` -- deletion
- `"OidMapWatcher watch disconnected unexpectedly, reconnecting in 5s"` -- watch error
- `"OidMapWatcher watch connection closed, reconnecting"` -- normal ~30min timeout

**DeviceWatcherService success path:**
- `"DeviceWatcher received {EventType} event for {ConfigMap}"` -- watch event arrived
- `"Device reload complete: {DeviceCount} devices"` -- reload succeeded

**DynamicPollScheduler (ReconcileAsync) detail logs:**
- `"Poll scheduler reconciled: +{Added} added, -{Removed} removed, ~{Rescheduled} rescheduled, {Total} total jobs"` -- diff summary

**DeviceWatcherService error path:**
- `"Failed to parse {ConfigKey} from ConfigMap {ConfigMap} -- skipping reload"` -- malformed JSON
- `"ConfigMap {ConfigMap} was deleted -- skipping reload, retaining current devices"` -- deletion
- `"DeviceWatcher watch disconnected unexpectedly, reconnecting in 5s"` -- watch error

### Test Data Strategy

**Baseline state (current ConfigMaps):**
- simetra-oidmaps: 92 OID entries (24 OBP + 68 NPB)
- simetra-devices: 2 devices (OBP-01 and NPB-01), each with 1 MetricPoll group

**OID map test additions -- use unused OID space:**
- Test OID: `"1.3.6.1.4.1.47477.10.99.1.0"` -> `"test_oid_added"` (OBP namespace, unused subtree .99)
- This OID won't resolve to a real SNMP value on the simulator, but the map entry will still be visible via OidMapService logs

**OID map rename test -- use existing OBP OID:**
- Change `"1.3.6.1.4.1.47477.10.21.1.3.1.0": "obp_link_state_L1"` to `"obp_link_state_L1_renamed"`
- Verify Prometheus shows `metric_name="obp_link_state_L1_renamed"` and `obp_link_state_L1` goes stale

**Device test addition -- fake device:**
- Add `"TEST-DEV-01"` with IP `"10.99.99.99"` (unreachable)
- Poll job will be created (visible in reconcile log: `+1 added`) but polls will fail with SNMP timeout
- This proves the Quartz job was scheduled without needing a real device

**Device test -- add/remove OIDs:**
- Remove half the OBP OIDs from OBP-01's Oids array
- Verify the removed OIDs no longer appear in new poll results

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Prometheus queries | Custom HTTP client code | `curl -s -G` + `jq` | Already proven in verify-e2e.sh |
| ConfigMap editing | Manual JSON construction | `kubectl get cm -o json \| jq ... \| kubectl apply -f -` | Prevents JSON typos |
| Log searching | Complex grep pipelines | `kubectl logs -l app=snmp-collector --since=2m \| grep "pattern"` | --since limits noise |
| Waiting for propagation | Fixed sleep | Poll with timeout (like wait_for_metric in verify-e2e.sh) | Avoids flaky timing |

## Common Pitfalls

### Pitfall 1: Prometheus Staleness Window
**What goes wrong:** After removing an OID or device, the old metric_name series still appears in Prometheus for ~5 minutes (Prometheus staleness marking).
**Why it happens:** Prometheus marks series stale only after 5 minutes of no new samples. The OTel -> Prometheus remote write path adds latency.
**How to avoid:** For "absence" checks, either:
1. Wait 5+ minutes before asserting absence (slow but reliable)
2. Use `snmp_gauge{metric_name="X"} offset 0s` and check the timestamp is old (faster)
3. Simply verify the NEW state is correct and note the old series will expire -- don't block on staleness
**Recommendation:** Verify the positive case (new metric_name appears) and just note that old series will go stale. Do not block tests waiting for absence.

### Pitfall 2: Leader-Gated Metrics
**What goes wrong:** Prometheus queries for `snmp_gauge` return 0 series because the pod you are checking is not the leader.
**Why it happens:** `snmp_gauge` and `snmp_info` are exported only by the leader pod (MetricRoleGatedExporter gates the "SnmpCollector.Leader" meter).
**How to avoid:** The Prometheus query aggregates across all pods automatically (Prometheus scrapes the OTel collector which receives from all pods, but only the leader sends business metrics). Just query Prometheus -- it has the leader's data.
**Warning signs:** If leader election is broken, no snmp_gauge data appears for any pod.

### Pitfall 3: Watch API vs Projected Volume Propagation
**What goes wrong:** Confusion between K8s watch API events and projected volume file updates.
**Why it happens:** The deployment uses projected volumes for the initial file load, but the watchers use the K8s API watch (not filesystem events). ConfigMap changes via kubectl apply trigger API events immediately but projected volume files update with a delay (kubelet sync period, typically 60-120s).
**How to avoid:** The watch API delivers events within 1-2 seconds. Don't confuse this with the slower projected volume sync. The watchers work correctly because they use the API, not filesystem watching.

### Pitfall 4: 3-Replica Log Checking
**What goes wrong:** `kubectl logs -l app=snmp-collector` returns logs from ALL 3 pods interleaved, making it hard to find the specific log line.
**Why it happens:** 3 replicas all receive the same watch event and all reload independently.
**How to avoid:** All 3 pods should log the same reload message. Use `--since=1m` to limit output. If you need per-pod logs, use `kubectl logs <pod-name> -n simetra`.

### Pitfall 5: kubectl apply Replaces Entire Data Key
**What goes wrong:** Using `kubectl apply` with a partial ConfigMap replaces the entire data section, accidentally removing keys.
**Why it happens:** ConfigMap apply is a full replacement of the data section.
**How to avoid:** Use `kubectl get cm simetra-oidmaps -n simetra -o json | jq '.data["oidmaps.json"] |= ...' | kubectl apply -f -` to modify the JSON content within the existing ConfigMap structure. Or use `kubectl edit cm` for interactive editing.

### Pitfall 6: JSON Within YAML Within kubectl
**What goes wrong:** Escaping issues when embedding modified JSON into ConfigMap patches.
**Why it happens:** The ConfigMap data value is a JSON string embedded in a YAML/JSON structure.
**How to avoid:** Use the jq pipeline approach:
```bash
# Safe pattern: extract -> modify -> apply
kubectl get cm simetra-oidmaps -n simetra -o json > /tmp/cm-backup.json
# Edit /tmp/cm-backup.json
kubectl apply -f /tmp/cm-backup.json
```

## Code Examples

### Prometheus Query: Check Metric Exists
```bash
# Source: deploy/k8s/verify-e2e.sh (check_metric function)
curl -s -G "http://localhost:9090/api/v1/query" \
  --data-urlencode 'query=snmp_gauge{metric_name="obp_r1_power_L1",device_name="OBP-01"}' | \
  jq '.data.result | length'
# Returns: number > 0 if metric exists
```

### Prometheus Query: Check Metric Absent (recent)
```bash
# Check no samples in last 2 minutes for a removed metric
curl -s -G "http://localhost:9090/api/v1/query" \
  --data-urlencode 'query=snmp_gauge{metric_name="obp_r1_power_L1"} unless snmp_gauge{metric_name="obp_r1_power_L1"} offset 5m' | \
  jq '.data.result | length'
# Not reliable due to staleness -- prefer positive assertions
```

### ConfigMap: Add OID Entry
```bash
# Get current ConfigMap, add entry, apply
kubectl get cm simetra-oidmaps -n simetra -o json | \
  jq '.data["oidmaps.json"] |= (fromjson | . + {"1.3.6.1.4.1.47477.10.99.1.0": "test_oid_added"} | tojson)' | \
  kubectl apply -f -
```

### ConfigMap: Remove OID Entry
```bash
# Get current ConfigMap, remove entry, apply
kubectl get cm simetra-oidmaps -n simetra -o json | \
  jq '.data["oidmaps.json"] |= (fromjson | del(.["1.3.6.1.4.1.47477.10.99.1.0"]) | tojson)' | \
  kubectl apply -f -
```

### ConfigMap: Rename Metric
```bash
# Change the metric name for an existing OID
kubectl get cm simetra-oidmaps -n simetra -o json | \
  jq '.data["oidmaps.json"] |= (fromjson | .["1.3.6.1.4.1.47477.10.21.1.3.1.0"] = "obp_link_state_L1_renamed" | tojson)' | \
  kubectl apply -f -
```

### ConfigMap: Push Malformed JSON
```bash
# Replace oidmaps.json with invalid JSON
kubectl get cm simetra-oidmaps -n simetra -o json | \
  jq '.data["oidmaps.json"] = "{ invalid json !!!"' | \
  kubectl apply -f -
```

### ConfigMap: Add Device Entry
```bash
# Add a test device (unreachable IP -- proves job creation)
kubectl get cm simetra-devices -n simetra -o json | \
  jq '.data["devices.json"] |= (fromjson | . + [{"Name":"TEST-DEV-01","IpAddress":"10.99.99.99","Port":161,"CommunityString":"Simetra.TEST-DEV-01","MetricPolls":[{"IntervalSeconds":30,"Oids":["1.3.6.1.4.1.47477.10.99.1.0"]}]}] | tojson)' | \
  kubectl apply -f -
```

### ConfigMap: Remove Device Entry
```bash
# Remove TEST-DEV-01
kubectl get cm simetra-devices -n simetra -o json | \
  jq '.data["devices.json"] |= (fromjson | map(select(.Name != "TEST-DEV-01")) | tojson)' | \
  kubectl apply -f -
```

### ConfigMap: Change Poll Interval
```bash
# Change OBP-01 poll interval from 10s to 30s
kubectl get cm simetra-devices -n simetra -o json | \
  jq '.data["devices.json"] |= (fromjson | map(if .Name == "OBP-01" then .MetricPolls[0].IntervalSeconds = 30 else . end) | tojson)' | \
  kubectl apply -f -
```

### ConfigMap: Delete Entirely
```bash
kubectl delete cm simetra-oidmaps -n simetra
```

### Check Pod Logs for Reload
```bash
# Check all pods for reload message in last 2 minutes
kubectl logs -n simetra -l app=snmp-collector --since=2m | grep -i "reload complete\|hot-reloaded\|reconciled"
```

### Check Pod Logs for Error
```bash
kubectl logs -n simetra -l app=snmp-collector --since=2m | grep -i "failed to parse\|skipping reload"
```

### Restore Original ConfigMap
```bash
# Re-apply from the repo source
kubectl apply -f deploy/k8s/snmp-collector/configmap.yaml
```

## Recommended Test Execution Order

1. **Baseline verification** -- confirm current metrics flowing (reuse verify-e2e.sh)
2. **OID map: Add OID** -- low risk, additive change
3. **OID map: Rename metric** -- tests UpdateMap diff logic
4. **OID map: Remove OID** -- tests fallback to "Unknown"
5. **OID map: Malformed JSON** -- error handling (restore after)
6. **OID map: Restore original** -- kubectl apply -f configmap.yaml
7. **Device: Add device** -- tests DynamicPollScheduler add
8. **Device: Change poll interval** -- tests reschedule
9. **Device: Remove device** -- tests job removal
10. **Device: Add/remove poll OIDs** -- tests OID list change
11. **Device: Malformed JSON** -- error handling (restore after)
12. **Device: Delete ConfigMap** -- deletion warning (recreate after)
13. **Watch reconnection** -- leave running 30+ min, verify reconnect log

**Wait times between steps:**
- After ConfigMap change: wait 5-10 seconds for watch event + processing
- Before Prometheus query: wait 15-20 seconds for OTel export cycle + Prometheus scrape
- For removal/absence: wait up to 5 minutes or skip negative assertion

## Recommended Prometheus Wait Strategy

The OTel -> Prometheus pipeline has a few latency stages:
1. Watch event received by watcher: ~1-2 seconds
2. Next poll cycle fires: up to IntervalSeconds (10s default)
3. OTel metric export interval: typically 10-30 seconds
4. Prometheus remote write ingestion: near-instant

**Total expected latency for a positive assertion: 15-30 seconds.**

Use a polling loop similar to verify-e2e.sh's `wait_for_metric` with a 60-second timeout.

## Open Questions

1. **OTel export interval configuration**
   - What we know: OTel collector receives OTLP gRPC, exports via prometheusremotewrite
   - What's unclear: The exact metric reader interval in the .NET app (typically 10s or 30s for periodic export)
   - Recommendation: Use 60-second timeout for positive Prometheus assertions; this is generous enough

2. **Device add with unreachable IP**
   - What we know: Adding TEST-DEV-01 at 10.99.99.99 will create Quartz jobs that fail on SNMP timeout
   - What's unclear: Whether SNMP timeout errors might cause excessive log noise or affect other poll jobs
   - Recommendation: Use it anyway -- the reconcile log proving `+1 added` is sufficient verification. Remove quickly after checking.

## Sources

### Primary (HIGH confidence)
- Source code: `OidMapWatcherService.cs` -- exact log messages, ConfigMap names, error handling
- Source code: `DeviceWatcherService.cs` -- exact log messages, ConfigMap names, error handling
- Source code: `OidMapService.cs` -- UpdateMap diff logging format
- Source code: `DynamicPollScheduler.cs` -- ReconcileAsync log format
- Source code: `deploy/k8s/snmp-collector/deployment.yaml` -- projected volume config
- Source code: `deploy/k8s/snmp-collector/configmap.yaml` -- baseline ConfigMap data
- Source code: `deploy/k8s/verify-e2e.sh` -- established Prometheus query pattern

### Secondary (MEDIUM confidence)
- Prometheus staleness: 5-minute default staleness window is standard Prometheus behavior

## Metadata

**Confidence breakdown:**
- Log message verification: HIGH -- read directly from source code
- kubectl patterns: HIGH -- standard K8s operations
- Prometheus query patterns: HIGH -- copied from existing verify-e2e.sh
- Staleness timing: MEDIUM -- standard Prometheus behavior, exact timing may vary
- OTel export latency: MEDIUM -- depends on configured export interval

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (stable -- source code patterns unlikely to change)
