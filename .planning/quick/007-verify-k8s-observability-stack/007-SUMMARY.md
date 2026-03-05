# Quick Task 007: Verify K8s Observability Stack

**Status:** Complete (verification only)
**Date:** 2026-03-05

## 1. Console Logs — Pod Comparison

All 3 pods produce identical log format: `{timestamp} [{level}] [{site}|{role}|{correlationId}] {category} {message}`

| Attribute | Pod 1 (59srm) | Pod 2 (9s48h) | Pod 3 (t96t5) |
|-----------|---------------|---------------|---------------|
| site_name | site-lab-k8s | site-lab-k8s | site-lab-k8s |
| role | **leader** | follower | follower |
| correlationId | Rotates ~30s | Rotates ~30s | Rotates ~30s |
| IP (in logs) | 10.1.2.130 | 10.1.2.131 | 10.1.2.132 |
| Health probes | /startup, /ready, /live — 200 | Same — 200 | Same — 200 |
| CorrelationJob | Visible, 30s interval | Visible, 30s interval | Visible, 30s interval |

**Key observations:**
- Exactly 1 leader (59srm), 2 followers — consistent throughout log window
- Correlation ID format: 32-char hex (GUID without hyphens)
- CorrelationJob shows dual-ID format `[...|correlationId|operationId]` on rotation lines — both equal (operationId = new correlationId)
- Health probes: readiness every ~10s, liveness every ~15s, all returning 200
- One anomaly on pod 2 (9s48h): occasional liveness probe taking ~1000ms (1 second exactly) — likely GC pause or first health check compilation

## 2. Elasticsearch Logs — Attributes Verification

**Index:** `simetra-logs` — 3,449 SnmpCollector documents (service_name: snmp-collector)

### Per-Pod Document Counts

| Pod | Docs | Notes |
|-----|------|-------|
| snmp-collector-5c7d779775-59srm | 1,098 | Current leader |
| snmp-collector-5c7d779775-9s48h | 1,092 | Follower |
| snmp-collector-5c7d779775-t96t5 | 929 | Follower (replacement, started later) |
| snmp-collector-5c7d779775-fqfms | 209 | Old leader (deleted during failover test) |
| 3x crashed pods (65c4957bb9-*) | 121 total | From initial CrashLoopBackOff |

### Log Scope Distribution (all pods combined)

| Scope | Count | Notes |
|-------|-------|-------|
| Microsoft.AspNetCore.Hosting.Diagnostics | 1,544 | Request start/finish |
| Microsoft.AspNetCore.Routing.EndpointMiddleware | 1,544 | Endpoint execute |
| SnmpCollector.Jobs.CorrelationJob | 159 | Correlation ID rotations |
| Quartz.Core.QuartzScheduler | 37 | Scheduler start/stop |
| Microsoft.Hosting.Lifetime | 32 | App start/stop |
| SnmpCollector.Lifecycle.GracefulShutdownService | 28 | Shutdown sequences |
| SnmpCollector.Telemetry.K8sLeaseElection | 13 | Leader acquisition/observation |
| SnmpCollector.Pipeline.* | Various | Pipeline initialization |

### Enrichment Attributes (verified on actual ES documents)

Every log document has these enrichment attributes from `SnmpLogEnrichmentProcessor`:

| Attribute | Example Value | Present |
|-----------|---------------|---------|
| `Attributes.site_name` | `site-lab-k8s` | All docs |
| `Attributes.role` | `leader` or `follower` | All docs |
| `Attributes.correlationId` | `842e5dcdf7bf4af8bc678fc9d7223225` | All docs |

Resource-level attributes:

| Attribute | Example Value | Present |
|-----------|---------------|---------|
| `Resource.service.name` | `snmp-collector` | All docs |
| `Resource.service.instance.id` | `snmp-collector-5c7d779775-59srm` | All docs |
| `Resource.telemetry.sdk.language` | `dotnet` | All docs |
| `Resource.telemetry.sdk.name` | `opentelemetry` | All docs |
| `Resource.telemetry.sdk.version` | `1.15.0` | All docs |

Additional attributes on request logs:

| Attribute | Present | Notes |
|-----------|---------|-------|
| `Attributes.Method`, `Path`, `Protocol` | Yes | HTTP request details |
| `Attributes.StatusCode` | Yes | Always 200 for health checks |
| `Attributes.ElapsedMilliseconds` | Yes | Request duration |
| `Attributes.TraceId`, `SpanId` | Yes | Distributed tracing context |
| `Attributes.ConnectionId`, `RequestId` | Yes | ASP.NET Core identifiers |

**Severity:** All 3,449 documents are `Information` level — no warnings or errors.

## 3. Prometheus Metrics — Labels Verification

### Metric Names (snmp-collector specific)

Runtime metrics (all 3 pods):
- `dotnet_assembly_count`
- `dotnet_exceptions_total`
- `dotnet_gc_collections_total`
- `dotnet_gc_heap_allocated_bytes_total`
- `dotnet_gc_last_collection_heap_fragmentation_size_bytes`
- `dotnet_gc_last_collection_heap_size_bytes`
- `dotnet_gc_last_collection_memory_committed_size_bytes`
- `dotnet_gc_pause_time_seconds_total`
- `dotnet_jit_compilation_time_seconds_total`
- `dotnet_jit_compiled_il_size_bytes_total`
- `dotnet_jit_compiled_methods_total`
- `dotnet_monitor_lock_contentions_total`
- `dotnet_process_cpu_count`
- `dotnet_process_cpu_time_seconds_total`
- `dotnet_process_memory_working_set_bytes`
- `dotnet_thread_pool_queue_length_total`
- `dotnet_thread_pool_thread_count_total`
- `dotnet_thread_pool_work_item_count_total`
- `dotnet_timer_count`

Pipeline/business metrics: None yet (expected — no SNMP device traffic)

### Labels on Every Metric (verified on `dotnet_process_cpu_count`)

| Label | Pod 1 (59srm) | Pod 2 (9s48h) | Pod 3 (t96t5) |
|-------|---------------|---------------|---------------|
| `__name__` | dotnet_process_cpu_count | Same | Same |
| `instance` | snmp-collector-5c7d779775-59srm | ...9s48h | ...t96t5 |
| `job` | snmp-collector | snmp-collector | snmp-collector |
| `service_instance_id` | snmp-collector-5c7d779775-59srm | ...9s48h | ...t96t5 |
| `service_name` | snmp-collector | snmp-collector | snmp-collector |
| `telemetry_sdk_language` | dotnet | dotnet | dotnet |
| `telemetry_sdk_name` | opentelemetry | opentelemetry | opentelemetry |
| `telemetry_sdk_version` | 1.15.0 | 1.15.0 | 1.15.0 |

**Note:** `instance` and `service_instance_id` are identical — both set to the pod name. This is expected because `resource_to_telemetry_conversion.enabled: true` in OTel Collector converts `service.instance.id` resource attribute to a Prometheus label, and the `instance` label is derived from the same value by the prometheusremotewrite exporter.

### 3 Distinct Instances Confirmed

All runtime metrics show exactly 3 distinct `service_instance_id` values matching the 3 running pod names.

## Findings

1. **Console logs:** Consistent format across all 3 pods. Role (leader/follower) correctly reflected. CorrelationId rotates every 30s on all pods independently.
2. **Elasticsearch:** 3,449 logs indexed with full OTel enrichment attributes (site_name, role, correlationId) present on every document. Resource attributes (service.name, service.instance.id, sdk info) all correct.
3. **Prometheus:** 19 runtime metrics from all 3 pods with consistent labels. `resource_to_telemetry_conversion` working (sdk labels propagated). No pipeline metrics yet (expected — no SNMP traffic).
