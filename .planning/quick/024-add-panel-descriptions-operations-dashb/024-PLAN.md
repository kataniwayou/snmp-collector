---
phase: quick
plan: 024
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-operations.json
autonomous: true

must_haves:
  truths:
    - "Every non-row panel shows an (i) icon that displays a tooltip describing the metric"
    - "Row panels have no description field added"
    - "Thread pool queries use _total suffix"
    - "Host Name dropdown filters the Pod dropdown"
  artifacts:
    - path: "deploy/grafana/dashboards/simetra-operations.json"
      provides: "Operations dashboard with panel descriptions"
      contains: "\"description\":"
  key_links: []
---

<objective>
Add tooltip descriptions to all non-row panels in the Simetra Operations dashboard and commit pending query fixes.

Purpose: Operators hovering the (i) icon on any panel will see a concise explanation of what the metric measures and where it comes from, improving dashboard usability.
Output: Updated dashboard JSON with descriptions on 18 panels, plus already-pending fixes committed.
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
  <name>Task 1: Add description field to every non-row panel</name>
  <files>deploy/grafana/dashboards/simetra-operations.json</files>
  <action>
For each non-row panel (type != "row"), add a "description" field at the top level of the panel object (sibling to "title", "type", etc.). Use the exact descriptions below:

**Pod Identity section:**
- id=2 "Pod Identity" (table): "Lists all running SnmpCollector pods with their instance ID, hostname, and Kubernetes node. Confirms replica count and distribution."

**Pipeline Counters section:**
- id=4 "Events Published": "Rate of SNMP events published to the processing channel by MetricPollJob and SnmpTrapListenerService. Source: snmp_event_published_total."
- id=5 "Events Handled": "Rate of events successfully processed through the MediatR pipeline. Source: snmp_event_handled_total."
- id=6 "Event Errors": "Rate of events that threw exceptions during pipeline processing, caught by ExceptionBehavior. Source: snmp_event_errors_total."
- id=7 "Events Rejected": "Rate of events that failed MediatR validation (e.g. missing OID, empty value). Source: snmp_event_validation_failed_total."
- id=8 "Polls Executed": "Rate of SNMP GET poll cycles completed by MetricPollJob. Source: snmp_poll_executed_total."
- id=9 "Traps Received": "Rate of SNMP trap PDUs received by SnmpTrapListenerService. Source: snmp_trap_received_total."
- id=10 "Trap Auth Failed": "Rate of traps rejected due to SNMP community string mismatch. Source: snmp_trap_auth_failed_total."
- id=11 "Trap Unknown Device": "Rate of traps received from IP addresses not in the configured device list. Source: snmp_trap_unknown_device_total."
- id=12 "Traps Dropped": "Rate of traps dropped because the processing channel was full. Source: snmp_trap_dropped_total."
- id=13 "Poll Unreachable": "Rate of poll attempts where the target device did not respond (SNMP timeout). Source: snmp_poll_unreachable_total."
- id=14 "Poll Recovered": "Rate of polls that succeeded after a device was previously marked unreachable. Source: snmp_poll_recovered_total."

**.NET Runtime section:**
- id=16 "GC Collections Rate": "Rate of .NET garbage collections by generation (Gen0, Gen1, Gen2). High Gen2 rates may indicate memory pressure. Source: dotnet_gc_collections_total."
- id=17 "GC Pause Time Rate": "Rate of time the application is paused for garbage collection. Sustained high values impact throughput. Source: dotnet_gc_pause_time_seconds_total."
- id=18 "Process Working Set": "Total physical memory (RSS) used by each pod. Source: dotnet_process_memory_working_set_bytes."
- id=19 "GC Heap Size": "Managed heap size after last GC, broken down by generation. Source: dotnet_gc_last_collection_heap_size_bytes."
- id=20 "Thread Pool Threads": "Number of active .NET thread pool threads. Source: dotnet_thread_pool_thread_count_total."
- id=21 "Thread Pool Queue Length": "Number of work items queued to the .NET thread pool. Sustained non-zero values indicate thread starvation. Source: dotnet_thread_pool_queue_length_total."

Do NOT add a "description" field to row panels (id=1, id=3, id=15).

The file on disk already contains pending fixes (Host Name dropdown, _total suffix on thread pool queries). Preserve those changes -- only add description fields.

Use a Python script to make the edits programmatically to avoid hand-editing a large JSON file:

```python
import json

with open('deploy/grafana/dashboards/simetra-operations.json', 'r') as f:
    dashboard = json.load(f)

descriptions = {
    2: "Lists all running SnmpCollector pods with their instance ID, hostname, and Kubernetes node. Confirms replica count and distribution.",
    4: "Rate of SNMP events published to the processing channel by MetricPollJob and SnmpTrapListenerService. Source: snmp_event_published_total.",
    5: "Rate of events successfully processed through the MediatR pipeline. Source: snmp_event_handled_total.",
    6: "Rate of events that threw exceptions during pipeline processing, caught by ExceptionBehavior. Source: snmp_event_errors_total.",
    7: "Rate of events that failed MediatR validation (e.g. missing OID, empty value). Source: snmp_event_validation_failed_total.",
    8: "Rate of SNMP GET poll cycles completed by MetricPollJob. Source: snmp_poll_executed_total.",
    9: "Rate of SNMP trap PDUs received by SnmpTrapListenerService. Source: snmp_trap_received_total.",
    10: "Rate of traps rejected due to SNMP community string mismatch. Source: snmp_trap_auth_failed_total.",
    11: "Rate of traps received from IP addresses not in the configured device list. Source: snmp_trap_unknown_device_total.",
    12: "Rate of traps dropped because the processing channel was full. Source: snmp_trap_dropped_total.",
    13: "Rate of poll attempts where the target device did not respond (SNMP timeout). Source: snmp_poll_unreachable_total.",
    14: "Rate of polls that succeeded after a device was previously marked unreachable. Source: snmp_poll_recovered_total.",
    16: "Rate of .NET garbage collections by generation (Gen0, Gen1, Gen2). High Gen2 rates may indicate memory pressure. Source: dotnet_gc_collections_total.",
    17: "Rate of time the application is paused for garbage collection. Sustained high values impact throughput. Source: dotnet_gc_pause_time_seconds_total.",
    18: "Total physical memory (RSS) used by each pod. Source: dotnet_process_memory_working_set_bytes.",
    19: "Managed heap size after last GC, broken down by generation. Source: dotnet_gc_last_collection_heap_size_bytes.",
    20: "Number of active .NET thread pool threads. Source: dotnet_thread_pool_thread_count_total.",
    21: "Number of work items queued to the .NET thread pool. Sustained non-zero values indicate thread starvation. Source: dotnet_thread_pool_queue_length_total.",
}

for panel in dashboard['panels']:
    pid = panel['id']
    if pid in descriptions:
        panel['description'] = descriptions[pid]

with open('deploy/grafana/dashboards/simetra-operations.json', 'w') as f:
    json.dump(dashboard, f, indent=2)
    f.write('\n')
```
  </action>
  <verify>
Run the Python script, then verify:
1. `python -c "import json; d=json.load(open('deploy/grafana/dashboards/simetra-operations.json')); panels=[p for p in d['panels'] if p['type']!='row']; assert all('description' in p for p in panels), 'Missing descriptions'; print(f'All {len(panels)} non-row panels have descriptions')"` -- confirms all 18 non-row panels have descriptions
2. `python -c "import json; d=json.load(open('deploy/grafana/dashboards/simetra-operations.json')); rows=[p for p in d['panels'] if p['type']=='row']; assert all('description' not in p for p in rows), 'Row has description'; print(f'{len(rows)} row panels correctly have no description')"` -- confirms row panels are untouched
3. JSON is valid (the json.load in verification scripts confirms this)
  </verify>
  <done>All 18 non-row panels have a "description" field with a concise tooltip explaining the metric. 3 row panels remain without descriptions. Dashboard JSON is valid.</done>
</task>

</tasks>

<verification>
- JSON parses without errors
- 18 non-row panels each have a "description" field
- 3 row panels (id 1, 3, 15) do NOT have a "description" field
- Pending fixes (Host Name dropdown, _total suffix) are preserved
- File ends with newline
</verification>

<success_criteria>
- Every non-row panel in simetra-operations.json has a description tooltip
- Dashboard JSON is valid and importable into Grafana
- All prior pending changes are preserved in the file
</success_criteria>

<output>
After completion, create `.planning/quick/024-add-panel-descriptions-operations-dashb/024-SUMMARY.md`
</output>
