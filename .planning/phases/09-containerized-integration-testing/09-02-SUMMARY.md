---
phase: 09-containerized-integration-testing
plan: 02
subsystem: infra
tags: [kubernetes, k8s, snmp-collector, configmap, deployment, service, leader-election, health-probes, downward-api]

# Dependency graph
requires:
  - phase: 08-graceful-shutdown-and-health-probes
    provides: Health probe endpoints at /healthz/startup, /healthz/ready, /healthz/live on port 8080
  - phase: 07-leader-election-and-role-gated-export
    provides: LeaseOptions (Name, Namespace) and SiteOptions.PodIdentity for leader election via K8s leases
  - phase: 09-containerized-integration-testing/09-01
    provides: Namespace, RBAC, and ServiceAccount (simetra-sa) in namespace simetra

provides:
  - SnmpCollector K8s ConfigMap (snmp-collector-config) with appsettings.Production.json overlay
  - SnmpCollector K8s Deployment (3 replicas, probes, Downward API, snmp-collector:local)
  - SnmpCollector K8s Service (ClusterIP, health port 8080)

affects:
  - 09-containerized-integration-testing (future plans using kubectl apply for snmp-collector)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ConfigMap provides appsettings.Production.json overlay via volumeMount subPath - same pattern as existing Simetra manifests"
    - "Downward API fieldRef metadata.name injects pod name into Site__PodIdentity env var for leader election identity"
    - "imagePullPolicy: Never for locally-built Docker images in Docker Desktop K8s cluster"
    - "Dummy device (127.0.0.1, empty MetricPolls) satisfies ReadinessHealthCheck DeviceChannelManager.DeviceNames.Count > 0 requirement"

key-files:
  created:
    - deploy/k8s/snmp-collector/configmap.yaml
    - deploy/k8s/snmp-collector/deployment.yaml
    - deploy/k8s/snmp-collector/service.yaml
  modified: []

key-decisions:
  - "Lease.Namespace=simetra in ConfigMap — RBAC grants simetra-sa lease access only in simetra namespace; default namespace would cause 403"
  - "Dummy device (dummy-device-01, 127.0.0.1, empty MetricPolls) — ReadinessHealthCheck requires DeviceChannelManager.DeviceNames.Count > 0; empty Devices list fails readiness"
  - "SnmpListener.Port=10162 — non-privileged port (not 162); containers run as non-root"
  - "No SNMP port exposed in Deployment — SNMP listener is internal; traps received within cluster, not from external sources in integration test"
  - "Volume name snmp-collector-config (not generic 'config') — avoids naming conflicts if multiple ConfigMaps mounted"

patterns-established:
  - "ConfigMap name matches volume name and K8s resource name: snmp-collector-config throughout all three manifests"
  - "Service exposes only health port (no SNMP port) — integration test drives traps internally"

# Metrics
duration: 1min
completed: 2026-03-05
---

# Phase 9 Plan 02: SnmpCollector K8s Manifests Summary

**Three SnmpCollector K8s manifests (ConfigMap, Deployment, Service) in deploy/k8s/snmp-collector/ enabling kubectl apply of a 3-replica leader-elected deployment with Downward API pod identity, health probes, and a dummy device for readiness.**

## Performance

- **Duration:** ~1 min
- **Started:** 2026-03-05T19:42:22Z
- **Completed:** 2026-03-05T19:43:20Z
- **Tasks:** 2
- **Files modified:** 3 (all created)

## Accomplishments

- ConfigMap `snmp-collector-config` provides complete appsettings.Production.json overlay with Lease.Namespace=simetra, dummy device for readiness probe, and OTLP endpoint pointing to otel-collector:4317
- Deployment runs 3 replicas with `imagePullPolicy: Never` for local Docker build, `serviceAccountName: simetra-sa`, and pod name injected via Downward API into `Site__PodIdentity` for leader election identity
- All three health probes (startup /healthz/startup, readiness /healthz/ready, liveness /healthz/live) configured on port 8080 matching Phase 8 endpoints
- Service exposes health port 8080 as ClusterIP

## Task Commits

Each task was committed atomically:

1. **Task 1: Create SnmpCollector ConfigMap** - `f8a63c4` (feat)
2. **Task 2: Create SnmpCollector Deployment and Service** - `501ba44` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `deploy/k8s/snmp-collector/configmap.yaml` - K8s ConfigMap providing appsettings.Production.json overlay with Lease, OTLP, dummy device, and SnmpListener settings
- `deploy/k8s/snmp-collector/deployment.yaml` - K8s Deployment: 3 replicas, snmp-collector:local, Downward API pod identity, all three health probes, ConfigMap volume mount
- `deploy/k8s/snmp-collector/service.yaml` - K8s ClusterIP Service exposing health port 8080

## Decisions Made

- **Lease.Namespace=simetra:** RBAC in 09-01 grants simetra-sa lease access only within the simetra namespace. Using "default" would cause 403 on lease acquisition.
- **Dummy device required:** ReadinessHealthCheck checks `DeviceChannelManager.DeviceNames.Count > 0`. An empty Devices array would leave readiness permanently unready. Dummy device (127.0.0.1, empty MetricPolls) satisfies the count check without triggering actual SNMP polls.
- **No SNMP port in Deployment:** Integration tests drive SNMP traps within the cluster. The SNMP listener is internal — no need to expose port 10162 via a K8s port or Service.
- **Volume name = snmp-collector-config:** Consistent naming across ConfigMap metadata, volumeMount name, and volumes entry prevents configuration drift.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required. Manifests are ready for `kubectl apply -f deploy/k8s/snmp-collector/`.

## Next Phase Readiness

- Three SnmpCollector manifests ready for `kubectl apply` as part of the 09-containerized-integration-testing stack
- Existing Simetra manifests in `deploy/k8s/` remain intact and unmodified
- Next: Apply full stack (namespace, RBAC, OTel collector, Prometheus, snmp-collector) and run integration verification

---
*Phase: 09-containerized-integration-testing*
*Completed: 2026-03-05*
