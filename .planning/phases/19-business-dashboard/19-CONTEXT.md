# Phase 19: Business Dashboard - Context

**Gathered:** 2026-03-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Create a business dashboard JSON file for Grafana that shows current SNMP gauge and info metric values for any device in dynamically-populated tables. No hardcoded device names. Dashboard JSON is a plain file — user imports it manually into Grafana UI.

</domain>

<decisions>
## Implementation Decisions

### Table layout
- Two tables stacked vertically, full width — gauge metrics table on top, info metrics table below
- Row headers (Grafana row panels) above each table: "Gauge Metrics" and "Info Metrics" — consistent with operations dashboard
- Gauge table columns in requirements order: service_instance_id, device_name, metric_name, oid, snmp_type, value
- Plain values in value column — no formatting, thresholds, or auto-units

### Query design
- Query across all pods (not filtered to leader only), though only leader will have gauge/info data
- One row per unique (device_name, metric_name) combination, showing the latest instant value
- Info metrics: extract label values into readable columns (not raw Prometheus labels)
- Info metrics: hide the value column (numeric 1 is meaningless) — show only label-derived columns (service_instance_id, device_name, metric_name, oid)

### Filtering & navigation
- Device filter dropdown populated from label_values(device_name)
- Multi-select enabled — can pick several devices or All
- Default to "All" (show every device on load)
- No pod filter on this dashboard — business dashboard focuses on devices, not pod infrastructure

### Dashboard structure
- Separate JSON file: deploy/grafana/dashboards/simetra-business.json
- Title: "Simetra Business", tags: ["simetra", "business"]
- Same patterns as operations dashboard: schemaVersion 39, __inputs/DS_PROMETHEUS, shared crosshair, editable
- Auto-refresh: 5 seconds, default time range: 15 minutes
- uid: "simetra-business"

### Claude's Discretion
- Exact PromQL queries for extracting info metric labels into columns
- Grafana transformations needed to reshape instant query results into table format
- Table panel heights and sizing
- Column widths and any rename overrides for readability

</decisions>

<specifics>
## Specific Ideas

- Dashboard is a plain JSON file — no K8s ConfigMaps, no automated provisioning
- User imports the JSON file manually into Grafana UI
- Prometheus datasource also configured manually by user (not provisioned)
- Follow same __inputs/${DS_PROMETHEUS} pattern as simetra-operations.json

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 19-business-dashboard*
*Context gathered: 2026-03-08*
