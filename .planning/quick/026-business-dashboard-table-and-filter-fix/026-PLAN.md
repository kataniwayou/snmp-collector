---
phase: quick-026
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-business.json
autonomous: true

must_haves:
  truths:
    - "Both tables hide service_instance_id, telemetry_sdk_language, telemetry_sdk_name, telemetry_sdk_version columns"
    - "Info table shows SNMP Type column (not hidden)"
    - "Both tables show k8s_pod_name as Pod Name"
    - "Dashboard has 3 cascading dropdown filters: Host -> Pod -> Device"
    - "Selecting a Host filters Pod dropdown to pods on that host"
    - "Selecting a Pod filters Device dropdown to devices on that pod"
    - "Table queries filter by all 3 variables"
  artifacts:
    - path: "deploy/grafana/dashboards/simetra-business.json"
      provides: "Updated business dashboard"
  key_links:
    - from: "templating.list[0] (host)"
      to: "templating.list[1] (pod)"
      via: "pod query filters by service_instance_id=~$host"
    - from: "templating.list[1] (pod)"
      to: "templating.list[2] (device)"
      via: "device query filters by k8s_pod_name=~$pod"
---

<objective>
Update the Simetra Business dashboard to clean up table columns and add cascading dropdown filters.

Purpose: Remove noisy telemetry SDK columns, add missing SNMP Type to info table, rename k8s_pod_name for readability, and provide Host/Pod/Device cascading filters for drill-down navigation.
Output: Updated simetra-business.json with refined tables and 3 cascading template variables.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@deploy/grafana/dashboards/simetra-business.json
</context>

<tasks>

<task type="auto">
  <name>Task 1: Update table column overrides and add cascading filters</name>
  <files>deploy/grafana/dashboards/simetra-business.json</files>
  <action>
    Modify the dashboard JSON with the following changes:

    **Column changes for BOTH Gauge and Info table panels:**

    1. Add `custom.hidden: true` overrides for these columns (add new override entries):
       - `service_instance_id` - change existing displayName override to `custom.hidden: true` instead
       - `telemetry_sdk_language`
       - `telemetry_sdk_name`
       - `telemetry_sdk_version`

    2. Add a new override for `k8s_pod_name` with `displayName: "Pod Name"` in both tables.

    3. Remove `service_instance_id` from both `indexByName` organize transformations (since it is now hidden). Shift remaining column indices down accordingly. Add `k8s_pod_name` to the organize indexByName with appropriate position (first visible column).

    **Info table specific:**

    4. For the Info table panel's snmp_type override: change from `custom.hidden: true` to `displayName: "SNMP Type"` (making it visible, matching gauge table behavior).

    5. Add `snmp_type` to the Info table's organize transformation indexByName at position 4 (after oid), and shift `value` to position 5.

    **Templating (cascading dropdowns):**

    6. Replace the single `device` variable in `templating.list` with 3 cascading variables in this order:

    Variable 1 - Host:
    ```json
    {
      "allValue": ".*",
      "current": { "selected": true, "text": "All", "value": "$__all" },
      "datasource": { "type": "prometheus", "uid": "${DS_PROMETHEUS}" },
      "definition": "label_values(snmp_gauge, service_instance_id)",
      "includeAll": true,
      "label": "Host",
      "multi": true,
      "name": "host",
      "query": { "qryType": 1, "query": "label_values(snmp_gauge, service_instance_id)" },
      "refresh": 2,
      "type": "query"
    }
    ```

    Variable 2 - Pod:
    ```json
    {
      "allValue": ".*",
      "current": { "selected": true, "text": "All", "value": "$__all" },
      "datasource": { "type": "prometheus", "uid": "${DS_PROMETHEUS}" },
      "definition": "label_values(snmp_gauge{service_instance_id=~\"$host\"}, k8s_pod_name)",
      "includeAll": true,
      "label": "Pod",
      "multi": true,
      "name": "pod",
      "query": { "qryType": 1, "query": "label_values(snmp_gauge{service_instance_id=~\"$host\"}, k8s_pod_name)" },
      "refresh": 2,
      "type": "query"
    }
    ```

    Variable 3 - Device:
    ```json
    {
      "allValue": ".*",
      "current": { "selected": true, "text": "All", "value": "$__all" },
      "datasource": { "type": "prometheus", "uid": "${DS_PROMETHEUS}" },
      "definition": "label_values(snmp_gauge{service_instance_id=~\"$host\", k8s_pod_name=~\"$pod\"}, device_name)",
      "includeAll": true,
      "label": "Device",
      "multi": true,
      "name": "device",
      "query": { "qryType": 1, "query": "label_values(snmp_gauge{service_instance_id=~\"$host\", k8s_pod_name=~\"$pod\"}, device_name)" },
      "refresh": 2,
      "type": "query"
    }
    ```

    7. Update both table panel queries to filter by all 3 variables:
       - Gauge: `snmp_gauge{service_instance_id=~"$host", k8s_pod_name=~"$pod", device_name=~"$device"}`
       - Info: `snmp_info{service_instance_id=~"$host", k8s_pod_name=~"$pod", device_name=~"$device"}`

    **Column ordering summary after changes:**

    Gauge table indexByName: k8s_pod_name: 0, device_name: 1, metric_name: 2, oid: 3, snmp_type: 4, Value #A: 5

    Info table indexByName: k8s_pod_name: 0, device_name: 1, metric_name: 2, oid: 3, snmp_type: 4, value: 5
  </action>
  <verify>
    Validate JSON is well-formed:
    ```bash
    python -c "import json; json.load(open('deploy/grafana/dashboards/simetra-business.json')); print('Valid JSON')"
    ```

    Verify key structural elements:
    ```bash
    python -c "
    import json
    d = json.load(open('deploy/grafana/dashboards/simetra-business.json'))
    t = d['templating']['list']
    assert len(t) == 3, f'Expected 3 variables, got {len(t)}'
    assert t[0]['name'] == 'host'
    assert t[1]['name'] == 'pod'
    assert t[2]['name'] == 'device'
    assert 'service_instance_id' in t[1]['query']['query'], 'Pod should cascade from host'
    assert 'k8s_pod_name' in t[2]['query']['query'], 'Device should cascade from pod'
    # Check gauge query has all 3 filters
    gauge_expr = d['panels'][1]['targets'][0]['expr']
    assert 'host' in gauge_expr and 'pod' in gauge_expr and 'device' in gauge_expr
    # Check info query has all 3 filters
    info_expr = d['panels'][3]['targets'][0]['expr']
    assert 'host' in info_expr and 'pod' in info_expr and 'device' in info_expr
    print('All assertions passed')
    "
    ```
  </verify>
  <done>
    - service_instance_id, telemetry_sdk_language, telemetry_sdk_name, telemetry_sdk_version are hidden in both tables
    - snmp_type is visible with "SNMP Type" display name in both tables
    - k8s_pod_name displays as "Pod Name" in both tables
    - 3 cascading dropdown variables exist: Host (service_instance_id) -> Pod (k8s_pod_name) -> Device (device_name)
    - Both table queries filter by all 3 variables
    - JSON is valid
  </done>
</task>

</tasks>

<verification>
- Dashboard JSON is valid and parseable
- 3 template variables with correct cascade chain
- Both table panels reference all 3 filter variables
- Column visibility and naming matches requirements
</verification>

<success_criteria>
- simetra-business.json updated with all column and filter changes
- JSON validates without errors
- Cascading filter chain: Host -> Pod -> Device
</success_criteria>

<output>
After completion, create `.planning/quick/026-business-dashboard-table-and-filter-fix/026-SUMMARY.md`
</output>
