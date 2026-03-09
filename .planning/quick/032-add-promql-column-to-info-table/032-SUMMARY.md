# Quick-032 Summary: Add PromQL Column to Info Table

## What Changed
- Wrapped info table Query A in `label_replace(label_join(...))` to construct `promql` label
- Each row shows copyable query: `snmp_info{metric_name="npb_model", device_name="NPB-01"}`
- Added `__tmp` hidden override and `promql` → "PromQL" rename override
- Updated `filterFieldsByName` to include `promql`
- Updated organize indexByName to place promql at position 6

## Files Modified
- `deploy/grafana/dashboards/simetra-business.json`

## Commit
- `65e01b7`: feat(quick-032): add PromQL column to info metrics table
