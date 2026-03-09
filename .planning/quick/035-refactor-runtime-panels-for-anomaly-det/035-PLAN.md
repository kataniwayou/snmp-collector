---
phase: quick
plan: 035
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-operations.json
autonomous: true

must_haves:
  truths:
    - "Operations dashboard .NET Runtime row shows CPU Time panel instead of GC Pause Time Rate"
    - "Operations dashboard .NET Runtime row shows Exceptions panel instead of GC Heap Size"
    - "Both new panels filter by k8s_pod_name=~$pod and break down per pod"
    - "Dashboard JSON is valid and importable into Grafana"
  artifacts:
    - path: "deploy/grafana/dashboards/simetra-operations.json"
      provides: "Updated operations dashboard with CPU Time and Exceptions panels"
      contains: "dotnet_process_cpu_time_seconds_total"
  key_links: []
---

<objective>
Replace two .NET Runtime panels in the operations dashboard for better anomaly detection: GC Pause Time Rate becomes CPU Time, GC Heap Size becomes Exceptions.

Purpose: CPU Time and Exceptions are faster, more actionable anomaly signals than GC internals.
Output: Updated simetra-operations.json with the two replacement panels.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@deploy/grafana/dashboards/simetra-operations.json
</context>

<tasks>

<task type="auto">
  <name>Task 1: Replace GC Pause Time and GC Heap Size panels with CPU Time and Exceptions</name>
  <files>deploy/grafana/dashboards/simetra-operations.json</files>
  <action>
    In the .NET Runtime row of the operations dashboard, make these two panel replacements.
    All other panel properties (gridPos, id, datasource, fieldConfig.defaults.custom, options, type) stay identical.

    **Panel 2 — GC Pause Time Rate (id 17, gridPos x=8 y=32) becomes CPU Time:**
    - Change `"title"` from `"GC Pause Time Rate"` to `"CPU Time"`
    - Change `"expr"` to: `sum by (k8s_pod_name) (rate(dotnet_process_cpu_time_seconds_total{k8s_pod_name=~"$pod"}[$__rate_interval]))`
    - Change `"unit"` from `"s"` to `"percentunit"`
    - Change `"description"` to: `"Rate of CPU time consumed per pod. Sustained spikes indicate runaway loops, stuck polls, or GC thrashing."`

    **Panel 4 — GC Heap Size (id 19, gridPos x=0 y=40) becomes Exceptions:**
    - Change `"title"` from `"GC Heap Size"` to `"Exceptions"`
    - Change `"expr"` to: `sum by (k8s_pod_name) (rate(dotnet_exceptions_total{k8s_pod_name=~"$pod"}[$__rate_interval]))`
    - Change `"unit"` from `"bytes"` to `"ops"`
    - Change `"description"` to: `"Rate of exceptions thrown per pod. Spikes indicate something broke - fastest anomaly detection signal."`

    Do NOT change any other panels. Do NOT change gridPos, id values, or panel ordering.
  </action>
  <verify>
    1. Run: `python -m json.tool deploy/grafana/dashboards/simetra-operations.json > /dev/null` — valid JSON
    2. Grep confirms new metrics: `grep -c "dotnet_process_cpu_time_seconds_total" deploy/grafana/dashboards/simetra-operations.json` returns 1
    3. Grep confirms new metrics: `grep -c "dotnet_exceptions_total" deploy/grafana/dashboards/simetra-operations.json` returns 1
    4. Grep confirms old metrics removed: `grep -c "gc_pause_time_seconds_total" deploy/grafana/dashboards/simetra-operations.json` returns 0
    5. Grep confirms old metrics removed: `grep -c "last_collection_heap_size" deploy/grafana/dashboards/simetra-operations.json` returns 0
    6. Grep confirms units: `grep "percentunit" deploy/grafana/dashboards/simetra-operations.json` returns a match
  </verify>
  <done>
    Dashboard JSON has 6 .NET Runtime panels: GC Collections Rate, CPU Time (percentunit), Process Working Set, Exceptions (ops), Thread Pool Threads, Thread Pool Queue Length. Old GC Pause Time and GC Heap Size panels are gone.
  </done>
</task>

</tasks>

<verification>
- `python -m json.tool deploy/grafana/dashboards/simetra-operations.json > /dev/null` exits 0
- Panel titles in .NET Runtime row match expected order of 6 panels
- No references to `gc_pause_time_seconds_total` or `last_collection_heap_size` remain
- New panels use `sum by (k8s_pod_name)` pattern consistent with other panels
</verification>

<success_criteria>
- Operations dashboard JSON is valid
- CPU Time panel uses `dotnet_process_cpu_time_seconds_total` with `percentunit`
- Exceptions panel uses `dotnet_exceptions_total` with `ops`
- All 6 .NET Runtime panels present with correct gridPos unchanged
</success_criteria>

<output>
After completion, create `.planning/quick/035-refactor-runtime-panels-for-anomaly-det/035-SUMMARY.md`
</output>
