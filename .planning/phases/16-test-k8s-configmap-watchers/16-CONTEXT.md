# Phase 16: Test K8s ConfigMap Watchers - Context

**Gathered:** 2026-03-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Verify OidMapWatcherService and DeviceWatcherService behavior by modifying ConfigMaps in the live K8s cluster and confirming changes take effect. This is live cluster verification — not unit tests with mocks.

</domain>

<decisions>
## Implementation Decisions

### Test approach
- Live K8s verification using kubectl edit/apply to modify ConfigMaps
- Verify via pod logs (reload messages) AND Prometheus queries (metric changes)
- No mocked IKubernetes or testcontainers — test against the running cluster

### OID map scenarios (simetra-oidmaps)
- **Add new OID**: Add an entry to oidmaps.json, verify it resolves to the new metric name on next poll cycle
- **Remove existing OID**: Remove an entry, verify it falls back to "Unknown" resolution
- **Rename metric**: Change the metric name for an existing OID, verify Prometheus shows the new name
- **Malformed JSON**: Push invalid JSON, verify watcher logs error and retains previous map

### Device scenarios (simetra-devices)
- **Add new device**: Add a device entry, verify new Quartz poll jobs appear and metrics flow to Prometheus
- **Remove device**: Remove a device, verify Quartz jobs are removed and polling stops
- **Add/remove poll OIDs**: Change the Oids array for an existing device, verify only the new set gets polled
- **Change poll interval**: Modify IntervalSeconds, verify Quartz reschedules the job

### Error & reconnection
- **Malformed devices JSON**: Push invalid JSON to devices.json, verify watcher logs error and retains current devices/jobs
- **Delete ConfigMap**: Delete simetra-oidmaps or simetra-devices entirely, verify watcher warns and retains current config
- **Watch reconnection**: Wait 30+ min for K8s watch timeout, verify watcher reconnects and continues working

### Verification method
- Every scenario verified by: (1) kubectl logs for reload/error messages, (2) Prometheus query to confirm metric changes
- Prometheus queries should check both presence and absence of expected metrics

### Claude's Discretion
- Order of test execution
- Specific OIDs/metric names used for test additions
- Wait times between ConfigMap changes and verification queries
- How to handle Prometheus staleness window for removed metrics

</decisions>

<specifics>
## Specific Ideas

- Use existing OBP/NPB OIDs as baseline — add/remove from those
- For "add new device", could use a third simulator instance or a fake device entry that will show poll failures (proving the job was created)
- Prometheus staleness: removed metrics may linger for ~5min. Verification should account for this.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 16-test-k8s-configmap-watchers*
*Context gathered: 2026-03-08*
