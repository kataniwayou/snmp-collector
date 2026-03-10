# Quick Task 039: Summary

## What Changed
- `deploy/grafana/dashboards/simetra-business.json`
  - Table 1 (gauge metrics): Unhid `source` column, set displayName "Source", width 60, index 4 after device_name
  - Table 2 (info metrics): Same changes
  - Both tables: shifted metric_name, oid, snmp_type, value columns +1 in indexByName

## Verification
- JSON validated with Python json.load — valid
