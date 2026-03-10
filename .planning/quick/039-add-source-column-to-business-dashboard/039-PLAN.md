# Quick Task 039: Add Source Column to Business Dashboard

## Objective
Add visible "Source" column to both tables (gauge metrics and info metrics) in the Grafana business dashboard, positioned after the Device column.

## Tasks

### Task 1: Unhide and configure Source column in both tables
- File: `deploy/grafana/dashboards/simetra-business.json`
- Change `source` field override from `custom.hidden: true` to `displayName: "Source"` + `custom.width: 60` in both table panels
- Add `source` to `indexByName` at position 4 (after `device_name` at 3), shift all subsequent columns +1

## Verification
- JSON remains valid
- Both tables show Source column after Device
