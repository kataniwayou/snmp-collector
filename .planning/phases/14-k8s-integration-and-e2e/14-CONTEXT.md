# Phase 14: K8s Integration and E2E - Context

**Gathered:** 2026-03-07
**Status:** Ready for planning

## Decisions

### MetricPoll Group Structure
- **One group per device** — single MetricPoll entry per device containing all OIDs
- **Poll interval: 10 seconds** — matches simulator counter/health update interval
- **All OIDs polled** — every OID from oidmap-obp.json (24) and oidmap-npb.json (68) included in MetricPoll
- **Explicit per-OID type** — each OID entry specifies its type (Gauge, Counter, Info) in the MetricPoll config

### ConfigMap Organization
- **Separate devices.json file** in the config directory, alongside oidmap-*.json files — auto-scanned, same mount pattern
- **.NET config limitation:** Devices is an array (not a dictionary like OidMap), so both device entries must be in a single file — arrays don't merge across files
- **K8s Service DNS names** for simulator addresses (e.g., `obp-simulator.simetra.svc.cluster.local`) — no placeholder IP replacement needed
- **Explicit CommunityString field** in each device entry — clear, no magic convention derivation

### E2E Verification Approach
- **Automated shell script** at `deploy/k8s/verify-e2e.sh`
- **Assumes cluster already running** — script only does port-forward + Prometheus API queries, user deploys separately
- **Checks metric existence + labels** — queries Prometheus API for metric names, verifies they exist with correct labels (device_name, site, oid_name)

### Trap Verification Scope
- **Full pipeline to Prometheus** — verify trap OIDs get resolved via OID map and appear as metrics in Prometheus
- **Wait for natural trap cycles** — no on-demand triggering, script waits with timeout for traps to arrive naturally
- **One trap metric per device type** — check for at least one OBP StateChange and one NPB portLinkChange metric
- **5 minute timeout** — covers worst-case 300s trap interval plus pipeline latency buffer

## Claude's Discretion

- Exact PromQL queries for the verification script
- Script output format (pass/fail summary, verbose logging)
- Whether to use `kubectl port-forward` or `NodePort` for Prometheus access
- K8s Service type for simulators (ClusterIP vs headless)
- devices.json key naming within the "Devices" section wrapper

## Deferred Ideas

None — discussion stayed within phase scope.
