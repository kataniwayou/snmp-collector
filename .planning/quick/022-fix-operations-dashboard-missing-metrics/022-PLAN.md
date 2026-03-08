---
phase: quick
plan: 022
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-operations.json
  - deploy/k8s/production/prometheus.yaml
autonomous: true

must_haves:
  truths:
    - "Runtime gauge panels (working set, heap size, thread count, queue length) display current values without rate()"
    - "Runtime counter panels (GC collections, GC pause time) display rate over time"
    - "Pipeline counter panels display rate over time"
    - "Production Prometheus can receive metrics via remote write"
  artifacts:
    - path: "deploy/grafana/dashboards/simetra-operations.json"
      provides: "Operations dashboard with correct metric names and query types"
    - path: "deploy/k8s/production/prometheus.yaml"
      provides: "Prometheus with remote write receiver enabled"
  key_links:
    - from: "dashboard panel queries"
      to: "Prometheus metric names"
      via: "OTel->Prometheus naming convention"
      pattern: "snmp_event_.*_total|dotnet_"
---

<objective>
Fix the Simetra Operations dashboard so pipeline and runtime metric panels display data.

Purpose: The operations dashboard has two categories of bugs preventing panels from showing data:
1. Runtime metric panels apply rate() to gauge-type metrics (UpDownCounters), which returns no data in Prometheus
2. Two runtime metric names have incorrect `_total` suffix (UpDownCounters are gauges, not counters)
3. Production Prometheus is missing `--web.enable-remote-write-receiver`, blocking ALL metric ingestion

Output: Corrected dashboard JSON and fixed production Prometheus deployment
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@deploy/grafana/dashboards/simetra-operations.json
@src/SnmpCollector/Telemetry/PipelineMetricService.cs
@src/SnmpCollector/Telemetry/TelemetryConstants.cs
@src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
@deploy/k8s/production/prometheus.yaml
@deploy/k8s/production/otel-collector.yaml
@deploy/k8s/monitoring/otel-collector-configmap.yaml
@deploy/k8s/monitoring/prometheus-deployment.yaml
</context>

<tasks>

<task type="auto">
  <name>Task 1: Fix runtime metric panel queries and names in dashboard JSON</name>
  <files>deploy/grafana/dashboards/simetra-operations.json</files>
  <action>
Fix 4 of 6 runtime metric panels that have incorrect queries. The .NET 9 System.Runtime meter emits
UpDownCounters (gauges in Prometheus) for memory, heap, and thread pool metrics. These MUST NOT use
rate() and some have incorrect `_total` suffixes.

**Panel fixes (find by title and expr):**

1. "Working Set Memory" panel (id likely around 16-17):
   - Current: `sum by (k8s_pod_name) (rate(dotnet_process_memory_working_set_bytes{k8s_pod_name=~"$pod"}[$__rate_interval]))`
   - Fix to: `dotnet_process_memory_working_set_bytes{k8s_pod_name=~"$pod"}`
   - Change unit from "ops" to "bytes"
   - legendFormat stays `{{k8s_pod_name}}`

2. "GC Heap Size" panel:
   - Current: `dotnet_gc_last_collection_heap_size_bytes{k8s_pod_name=~"$pod"}`
   - This one is actually correct (no rate, correct name). Verify and leave as-is.

3. "Thread Pool Threads" panel:
   - Current: `dotnet_thread_pool_thread_count_total{k8s_pod_name=~"$pod"}`
   - Fix to: `dotnet_thread_pool_thread_count{k8s_pod_name=~"$pod"}`
   - Remove `_total` suffix (UpDownCounter, not Counter)
   - The unit should NOT be "ops" -- remove or set to "short"

4. "Thread Pool Queue Length" panel:
   - Current: `dotnet_thread_pool_queue_length_total{k8s_pod_name=~"$pod"}`
   - Fix to: `dotnet_thread_pool_queue_length{k8s_pod_name=~"$pod"}`
   - Remove `_total` suffix (UpDownCounter, not Counter)
   - The unit should NOT be "ops" -- remove or set to "short"

**Important context on OTel -> Prometheus naming:**
- .NET 9 `System.Runtime` UpDownCounter instruments map to Prometheus gauges (no `_total` suffix)
- .NET 9 `System.Runtime` Counter instruments map to Prometheus counters (with `_total` suffix)
- Dots in OTel metric names become underscores in Prometheus
- Unit suffixes: `By` -> `_bytes`, `s` -> `_seconds`

**Do NOT change:**
- The 2 runtime Counter panels: `dotnet_gc_collections_total` and `dotnet_gc_pause_time_seconds_total` -- these ARE counters and rate() is correct
- Any pipeline counter panels (all 11 use correct Counter names with `_total` and rate() is correct)
- The pod identity table panel
- Template variable definitions
- Datasource references

**For each gauge panel being fixed, also update the fieldConfig:**
- Remove the rate()-incompatible threshold of 80 if present (makes no sense for absolute values)
- Set appropriate units:
  - Working Set Memory: "bytes"
  - Thread Pool Threads: "short" (count)
  - Thread Pool Queue Length: "short" (count)
  </action>
  <verify>
    Validate JSON is well-formed:
    ```bash
    python3 -c "import json; json.load(open('deploy/grafana/dashboards/simetra-operations.json')); print('Valid JSON')"
    ```
    Then grep for the fixed expressions:
    ```bash
    grep -n "dotnet_thread_pool_thread_count[^_]" deploy/grafana/dashboards/simetra-operations.json
    grep -n "dotnet_thread_pool_queue_length[^_]" deploy/grafana/dashboards/simetra-operations.json
    grep -n "dotnet_process_memory_working_set_bytes" deploy/grafana/dashboards/simetra-operations.json
    ```
    Verify NO rate() on gauge metrics:
    ```bash
    grep -c "rate(dotnet_process_memory" deploy/grafana/dashboards/simetra-operations.json  # should be 0
    grep -c "rate(dotnet_thread_pool" deploy/grafana/dashboards/simetra-operations.json  # should be 0
    grep -c "rate(dotnet_gc_last_collection" deploy/grafana/dashboards/simetra-operations.json  # should be 0
    ```
  </verify>
  <done>
    - 4 runtime gauge panels use direct metric queries without rate()
    - 2 runtime metric names corrected (removed erroneous `_total` suffix)
    - 2 runtime counter panels unchanged (rate() is correct for counters)
    - All 11 pipeline counter panels unchanged
    - Dashboard JSON is valid
  </done>
</task>

<task type="auto">
  <name>Task 2: Fix production Prometheus missing remote-write-receiver flag</name>
  <files>deploy/k8s/production/prometheus.yaml</files>
  <action>
The production Prometheus deployment is missing `--web.enable-remote-write-receiver` in its args.
Without this flag, the OTel Collector's `prometheusremotewrite` exporter pushes to
`http://prometheus:9090/api/v1/write` but Prometheus rejects the writes silently.

The dev Prometheus (deploy/k8s/monitoring/prometheus-deployment.yaml) correctly has this flag.

In `deploy/k8s/production/prometheus.yaml`, add the missing arg to the Prometheus container:

```yaml
args:
  - "--config.file=/etc/prometheus/prometheus.yml"
  - "--storage.tsdb.retention.time=30d"
  - "--web.enable-remote-write-receiver"
```

Also note: the production Prometheus has a scrape_config targeting `otel-collector:8889`, but the OTel
Collector only has a `prometheusremotewrite` exporter (not a `prometheus` exporter), so port 8889
has nothing to scrape. This scrape config is harmless but unnecessary. Leave it as-is for now since
the remote write fix is the critical change. Add a YAML comment noting it's a dead target.

Do NOT change the dev Prometheus (deploy/k8s/monitoring/) -- it already has the flag.
  </action>
  <verify>
    ```bash
    grep "enable-remote-write-receiver" deploy/k8s/production/prometheus.yaml
    ```
  </verify>
  <done>
    - Production Prometheus args include `--web.enable-remote-write-receiver`
    - Remote write path from OTel Collector to Prometheus is unblocked
  </done>
</task>

</tasks>

<verification>
1. Dashboard JSON is valid and parseable
2. All 6 runtime panels have correct metric names and query types:
   - 2 Counter panels: rate() with `_total` suffix (unchanged)
   - 4 Gauge panels: direct query without rate(), no `_total` suffix for thread pool metrics
3. All 11 pipeline counter panels unchanged (already correct)
4. Production Prometheus has remote write receiver enabled
5. After redeploying Prometheus and reimporting dashboard, user verifies panels show data
</verification>

<success_criteria>
- Dashboard JSON valid with corrected runtime metric queries
- Production Prometheus deployment includes `--web.enable-remote-write-receiver`
- User can reimport dashboard via Grafana UI and see data on all panels
</success_criteria>

<output>
After completion, create `.planning/quick/022-fix-operations-dashboard-missing-metrics/022-SUMMARY.md`
</output>
