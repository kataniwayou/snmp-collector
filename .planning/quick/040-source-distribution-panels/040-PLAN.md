# Quick Task 040: Source Distribution Panels

## Objective
Add two visualization panels to the business dashboard showing poll/trap source distribution:
1. Time series rate panel — `sum by (source) (rate(snmp_event_handled_total[1m]))` — shows throughput over time
2. Pie/donut chart — `count by (source) ({source=~".+"})` — shows current series count ratio

## Tasks

### Task 1: Add panels to simetra-business.json
- File: `deploy/grafana/dashboards/simetra-business.json`
- Add "Source Distribution" row header at y=22
- Add time series panel (w=18, h=8) at y=23 — event rate by source with poll=blue, trap=orange
- Add pie chart panel (w=6, h=8) at y=23 x=18 — donut showing series count ratio
- Add timeseries and piechart to `__requires`
- Both panels use $host and $pod template variables

## Verification
- JSON valid
- Three new panel objects in panels array
