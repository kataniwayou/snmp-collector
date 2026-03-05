---
phase: 09-containerized-integration-testing
verified: 2026-03-05T00:00:00Z
status: human_needed
score: 10/10 structural must-haves verified
human_verification:
  - test: Run full DEPLOY.md steps 1-10 on Docker Desktop K8s
    expected: 3 snmp-collector pods reach Running/Ready 1/1, runtime metrics visible in Prometheus with 3 distinct service_instance_id values, lease holderIdentity shows exactly one pod, failover within ~15 seconds
    why_human: Requires a live Docker Desktop K8s cluster; 09-03-SUMMARY documents human verification was completed and passed, but the verifier cannot independently confirm a live cluster run
---

# Phase 9: Containerized Integration Testing Verification Report

**Phase Goal:** Verify SnmpCollector runs correctly in K8s with 3 replicas, runtime metrics flow to Prometheus, and leader election works
**Verified:** 2026-03-05
**Status:** human_needed
**Re-verification:** No -- initial verification

---

## Goal Achievement

### Observable Truths

| #  | Truth | Status | Evidence |
|----|-------|--------|----------|
| 1  | OTel Collector uses prometheusremotewrite exporter pushing to http://prometheus:9090/api/v1/write | VERIFIED | otel-collector-configmap.yaml lines 15-16: prometheusremotewrite exporter with endpoint http://prometheus:9090/api/v1/write |
| 2  | Prometheus accepts remote_write pushes via --web.enable-remote-write-receiver flag | VERIFIED | prometheus-deployment.yaml line 23: --web.enable-remote-write-receiver in args |
| 3  | OTel Collector no longer exposes port 8889 for Prometheus scraping | VERIFIED | grep for 8889 across deploy/k8s/monitoring/ returns no matches; only port 4317 in container spec and Service |
| 4  | Prometheus no longer scrapes OTel Collector (no scrape_configs for otel-collector:8889) | VERIFIED | prometheus-configmap.yaml is 10 lines -- global block only, no scrape_configs section |
| 5  | resource_to_telemetry_conversion is enabled so OTel resource attributes propagate as Prometheus labels | VERIFIED | otel-collector-configmap.yaml lines 17-18: resource_to_telemetry_conversion.enabled: true inside prometheusremotewrite block |
| 6  | SnmpCollector ConfigMap provides appsettings.Production.json overlay with Lease.Namespace=simetra, dummy device, OTLP:4317 | VERIFIED | configmap.yaml: Namespace simetra in Lease, dummy-device-01, Endpoint http://otel-collector:4317; key appsettings.k8s.json mounted as /app/appsettings.Production.json via subPath |
| 7  | SnmpCollector Deployment runs 3 replicas with serviceAccountName=simetra-sa and imagePullPolicy=Never | VERIFIED | deployment.yaml: replicas: 3, serviceAccountName: simetra-sa, imagePullPolicy: Never, image: snmp-collector:local |
| 8  | Each pod receives its own name via Downward API into Site__PodIdentity for leader election identity | VERIFIED | deployment.yaml lines 31-34: Site__PodIdentity env var from fieldRef fieldPath: metadata.name |
| 9  | Health probes configured on port 8080 | VERIFIED | deployment.yaml: all three httpGet probes on port health (8080) with correct paths matching Phase 8 endpoints |
| 10 | Deployment guide exists with docker build, kubectl apply, Prometheus queries, and leader election commands | VERIFIED | DEPLOY.md: 180 lines, 10 steps; docker build command, kubectl apply for all manifests, PromQL queries, holderIdentity inspection and failover commands |

**Score:** 10/10 structural truths verified

The 11th truth -- Human verification confirms: pods running, probes healthy, metrics in Prometheus, leader elected -- cannot be verified programmatically. See Human Verification Required below.

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| deploy/k8s/monitoring/otel-collector-configmap.yaml | OTel config with prometheusremotewrite | VERIFIED | 37 lines; prometheusremotewrite exporter, endpoint, resource_to_telemetry_conversion, metrics pipeline |
| deploy/k8s/monitoring/otel-collector-deployment.yaml | OTel deployment without port 8889 | VERIFIED | 55 lines; port 4317 only in container and Service; no 8889 anywhere |
| deploy/k8s/monitoring/prometheus-configmap.yaml | Prometheus push-only global block | VERIFIED | 10 lines; global block only, no scrape_configs section |
| deploy/k8s/monitoring/prometheus-deployment.yaml | Prometheus deployment with remote-write-receiver | VERIFIED | 58 lines; args contain --web.enable-remote-write-receiver and --config.file |
| deploy/k8s/snmp-collector/configmap.yaml | SnmpCollector appsettings overlay | VERIFIED | 46 lines; Lease.Namespace=simetra, dummy-device-01, OTLP endpoint, SnmpListener |
| deploy/k8s/snmp-collector/deployment.yaml | 3-replica deployment with probes and Downward API | VERIFIED | 70 lines; replicas=3, all three probes on health:8080, Site__PodIdentity, simetra-sa |
| deploy/k8s/snmp-collector/service.yaml | ClusterIP service on port 8080 | VERIFIED | 16 lines; type: ClusterIP, port 8080 named health, selector app=snmp-collector |
| deploy/k8s/snmp-collector/DEPLOY.md | Deployment and validation guide | VERIFIED | 180 lines; 10 steps with exact commands, PromQL queries, failover test |
| .dockerignore | Prevents Windows obj/ leaking into Linux build | VERIFIED | 8 lines; entries for **/obj/ and **/bin/ present |
| src/SnmpCollector/SnmpCollector.csproj | Content items for appsettings.json publish | VERIFIED | Line 16: Content Include appsettings*.json CopyToOutputDirectory=PreserveNewest CopyToPublishDirectory=PreserveNewest |
| src/SnmpCollector/Dockerfile | Clean multi-stage build without broken sed | VERIFIED | 48 lines; SDK->aspnet stages, no sed command, non-root USER |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| otel-collector-configmap.yaml | Prometheus service | prometheusremotewrite.endpoint | WIRED | http://prometheus:9090/api/v1/write in exporters block |
| otel-collector-configmap.yaml | metrics pipeline | service.pipelines.metrics.exporters | WIRED | exporters: [prometheusremotewrite] wired to metrics pipeline |
| prometheus-deployment.yaml | prometheus-configmap.yaml | --config.file arg + volumeMount subPath | WIRED | --config.file=/etc/prometheus/prometheus.yml + volumeMount subPath prometheus.yml |
| prometheus-deployment.yaml | remote_write receiver | --web.enable-remote-write-receiver arg | WIRED | arg present in container spec args list |
| snmp-collector/deployment.yaml | snmp-collector/configmap.yaml | volume snmp-collector-config + subPath | WIRED | volume name matches ConfigMap name; subPath appsettings.k8s.json mounts to /app/appsettings.Production.json |
| snmp-collector/configmap.yaml | otel-collector Service | Otlp.Endpoint in JSON | WIRED | Endpoint http://otel-collector:4317 in appsettings.k8s.json |
| snmp-collector/deployment.yaml | deploy/k8s/serviceaccount.yaml | serviceAccountName: simetra-sa | WIRED | simetra-sa in serviceaccount.yaml; rbac.yaml grants lease CRUD in simetra namespace |
| snmp-collector/deployment.yaml | Site__PodIdentity env var | Downward API fieldRef metadata.name | WIRED | fieldPath: metadata.name injects pod name for lease holder identity |
| DEPLOY.md Step 3 | src/SnmpCollector/Dockerfile | docker build command | WIRED | docker build -f src/SnmpCollector/Dockerfile -t snmp-collector:local . |
| DEPLOY.md Step 9 | Prometheus | PromQL runtime + pipeline queries | WIRED | process_runtime_dotnet_gc_collections_count_total and snmp_event_published_total present |
| DEPLOY.md Step 10 | K8s lease failover | kubectl delete pod + lease watch | WIRED | holderIdentity inspection and failover commands with ~15s expected timing |

---

## Anti-Patterns Found

No anti-patterns in Phase 9 artifacts. No TODO/FIXME/placeholder strings in any manifest or guide.

Note: deploy/k8s/simulators/configmap-devices.yaml contains PLACEHOLDER_* strings for runtime IP substitution -- this file is out of Phase 9 scope and the placeholders are intentional, not a gap.

---

## Human Verification Required

### 1. End-to-End K8s Integration Test

**Test:** Follow DEPLOY.md steps 1-10 on a Docker Desktop K8s cluster:

1. Apply monitoring stack (namespace, RBAC, OTel Collector, Prometheus)
2. Build Docker image: docker build -f src/SnmpCollector/Dockerfile -t snmp-collector:local .
3. Apply SnmpCollector manifests: configmap.yaml, deployment.yaml, service.yaml
4. Watch pods: confirm 3 snmp-collector pods reach Running / READY 1/1
5. Port-forward Prometheus, query process_runtime_dotnet_gc_collections_count_total -- expect 3 distinct service_instance_id values
6. Inspect lease holderIdentity -- expect exactly one pod name
7. Delete the leader pod, confirm a different pod acquires the lease within ~15 seconds

**Expected:**
- All 3 pods Running/Ready with zero restarts
- Runtime dotnet_* metrics visible in Prometheus from 3 distinct instances
- Exactly one lease holder at any given time
- Failover completes within the 15-second lease duration

**Why human:** Requires a live Docker Desktop K8s cluster. The 09-03-SUMMARY documents that all 10 steps were executed and passed during Phase 9 execution (3/3 pods READY 1/1, metrics from 3 instances, lease held by one pod, failover within ~15 seconds). The verifier cannot independently confirm current live cluster state. All structural wiring is fully verified -- this step confirms runtime behavior.

---

## Phase Outcome

All structural must-haves are verified. Every manifest file exists, is substantive, and is correctly wired:

- Push pipeline: OTel Collector prometheusremotewrite -> Prometheus /api/v1/write is wired end-to-end in config
- Port 8889 is fully eliminated from all monitoring manifests
- SnmpCollector 3-replica deployment has correct Downward API pod identity, all three health probes, simetra-sa service account, and ConfigMap volume mount
- Docker build prerequisites (.dockerignore, appsettings.json Content items, clean Dockerfile) are in place
- DEPLOY.md provides a complete guide with exact commands for build, deploy, validate, and failover

The 09-03-SUMMARY reports human verification passed with all 10 steps confirmed. If that cluster run reflects current state, the phase goal is fully achieved.

---

_Verified: 2026-03-05_
_Verifier: Claude (gsd-verifier)_