---
phase: quick
plan: 027
type: execute
wave: 1
depends_on: []
files_modified:
  - simulators/npb/npb_simulator.py
  - simulators/obp/obp_simulator.py
  - src/SnmpCollector/config/oidmaps.json
  - deploy/grafana/dashboards/simetra-operations.json
  - deploy/grafana/dashboards/simetra-business.json
autonomous: true

must_haves:
  truths:
    - "NPB system health metrics (cpu_util, mem_util, sys_temp, uptime) emit as numeric SNMP types, not OctetString"
    - "NPB and OBP simulators serve static info OIDs (model, serial, sw_version) as OctetString"
    - "oidmaps.json maps all 6 new static info OIDs to named metrics"
  artifacts:
    - path: "simulators/npb/npb_simulator.py"
      provides: "Gauge32/TimeTicks system health + 3 static info OIDs"
      contains: "v2c.Gauge32"
    - path: "simulators/obp/obp_simulator.py"
      provides: "3 static NMU info OIDs"
      contains: "obp_device_type"
    - path: "src/SnmpCollector/config/oidmaps.json"
      provides: "OID name mappings for 6 new static OIDs"
      contains: "npb_model"
  key_links:
    - from: "simulators/npb/npb_simulator.py"
      to: "src/SnmpCollector/config/oidmaps.json"
      via: "OID paths must match exactly"
      pattern: "47477\\.100\\.1\\.[567]\\.0"
    - from: "simulators/obp/obp_simulator.py"
      to: "src/SnmpCollector/config/oidmaps.json"
      via: "OID paths must match exactly"
      pattern: "47477\\.10\\.21\\.60\\."
---

<objective>
Fix NPB simulator system health metrics from OctetString to numeric SNMP types (Gauge32/TimeTicks) so they emit as snmp_gauge instead of snmp_info. Add static info OIDs (model, serial, sw_version) to both NPB and OBP simulators. Update oidmaps.json with all new OID mappings. Commit pending dashboard fixes alongside.

Purpose: System health metrics currently create duplicate info rows in Grafana. Numeric types will classify correctly as gauges. Static info OIDs provide device identity metadata.
Output: Updated simulators + oidmaps.json + dashboard fixes, all committed together.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@simulators/npb/npb_simulator.py
@simulators/obp/obp_simulator.py
@src/SnmpCollector/config/oidmaps.json
</context>

<tasks>

<task type="auto">
  <name>Task 1: NPB simulator — convert system health to numeric types and add static info OIDs</name>
  <files>simulators/npb/npb_simulator.py</files>
  <action>
  **Part A: Change SYSTEM_METRICS types (line 96-101)**

  Change from OctetString to numeric types:
  ```python
  SYSTEM_METRICS = [
      (1, "cpu_util",  v2c.Gauge32),     # CPU % x10 (e.g., 150 = 15.0%)
      (2, "mem_util",  v2c.Gauge32),     # Memory % x10
      (3, "sys_temp",  v2c.Gauge32),     # Temperature C x10 (e.g., 425 = 42.5C)
      (4, "uptime",    v2c.TimeTicks),   # Centiseconds (standard SNMP uptime)
  ]
  ```

  **Part B: Change system_state initial values (line 118-123)**

  Change from string values to integers matching the new types:
  ```python
  system_state = {
      "cpu_util": 150,    # 15.0% x10
      "mem_util": 450,    # 45.0% x10
      "sys_temp": 420,    # 42.0C x10
      "uptime": 0,        # centiseconds
  }
  ```

  **Part C: Update update_system_health() (line 322-341)**

  After the random walk floats are computed, store integer values instead of formatted strings:
  ```python
  system_state["cpu_util"] = int(_health_floats["cpu_util"] * 10)
  system_state["mem_util"] = int(_health_floats["mem_util"] * 10)
  system_state["sys_temp"] = int(_health_floats["sys_temp"] * 10)
  system_state["uptime"] = int(_health_floats["uptime_int"] * 100)  # centiseconds
  ```

  **Part D: Add 3 static info OIDs**

  After the SYSTEM_METRICS OID registration loop (after line 200, before per-port metrics), add static OID registration. These are NOT in SYSTEM_METRICS because they never update:

  ```python
  # Static info OIDs: 47477.100.1.{5,6,7}.0 -- device identity, never change
  STATIC_INFO = [
      (5, "npb_model",      "NPB-2E"),
      (6, "npb_serial",     "SN-NPB-001"),
      (7, "npb_sw_version", "5.2.4"),
  ]

  for metric_id, label, value in STATIC_INFO:
      oid_str = f"{NPB_PREFIX}.1.{metric_id}"
      oid_tuple = oid_str_to_tuple(oid_str)
      symbols[f"info_scalar_{metric_id}"] = MibScalar(oid_tuple, v2c.OctetString())
      symbols[f"info_instance_{metric_id}"] = DynamicInstance(
          oid_tuple, (0,), v2c.OctetString(), lambda v=value: v
      )
      oid_count += 1
  ```

  **Part E: Update docstring and log line**

  Update the module docstring (line 4-6) to reflect "4 system metrics ... + 3 static info OIDs + 64 per-port metrics = 71 OIDs".
  Update the log line at line 218 to say "Registered %d poll OIDs (4 system + 3 info + 8 ports x 8 metrics)".
  Update the log at line 459 similarly.
  </action>
  <verify>Run `python -c "import ast; ast.parse(open('simulators/npb/npb_simulator.py').read()); print('OK')"` to verify syntax. Grep for "Gauge32" and "STATIC_INFO" to confirm changes landed.</verify>
  <done>SYSTEM_METRICS uses Gauge32/TimeTicks. system_state holds integers. update_system_health stores int(float*10) / int(float*100). 3 static info OIDs registered. No OctetString remains for system health metrics.</done>
</task>

<task type="auto">
  <name>Task 2: OBP simulator — add static NMU info OIDs</name>
  <files>simulators/obp/obp_simulator.py</files>
  <action>
  Add 3 static OctetString OIDs for NMU-level device identity. These use OBP NMU OID path: 1.3.6.1.4.1.47477.10.21.60.{suffix}.0

  After the existing OID registration loop (after line 160, after `mibBuilder.export_symbols`), add:

  ```python
  # ---------------------------------------------------------------------------
  # Static NMU info OIDs: BYPASS_PREFIX suffix .60.{1,13,15}.0
  # ---------------------------------------------------------------------------
  NMU_PREFIX = f"{BYPASS_PREFIX}.60"
  STATIC_NMU_INFO = [
      (1,  "obp_device_type",  "OBP4"),
      (13, "obp_sw_version",   "v2.1.0"),
      (15, "obp_serial",       "SN-OBP-001"),
  ]

  nmu_symbols = {}
  for suffix, label, value in STATIC_NMU_INFO:
      oid_str = f"{NMU_PREFIX}.{suffix}"
      oid_tuple = oid_str_to_tuple(oid_str)
      nmu_symbols[f"nmu_scalar_{suffix}"] = MibScalar(oid_tuple, v2c.OctetString())
      nmu_symbols[f"nmu_instance_{suffix}"] = DynamicInstance(
          oid_tuple, (0,), v2c.OctetString(), lambda v=value: v
      )
      registered_oids.append(f"{oid_str}.0")

  mibBuilder.export_symbols("__OBP-NMU-MIB", **nmu_symbols)
  ```

  IMPORTANT: This must go AFTER the first `mibBuilder.export_symbols("__OBP-SIM-MIB", **symbols)` call. Use a separate `nmu_symbols` dict and a separate `export_symbols` call with a different MIB name (`"__OBP-NMU-MIB"`).

  Update the module docstring (lines 1-7) to mention "24 poll OIDs + 3 NMU info OIDs = 27 OIDs".
  Update log line at 278 to say "27 OIDs" instead of "24 OIDs".
  </action>
  <verify>Run `python -c "import ast; ast.parse(open('simulators/obp/obp_simulator.py').read()); print('OK')"` to verify syntax. Grep for "NMU_PREFIX" and "STATIC_NMU_INFO" to confirm.</verify>
  <done>3 static NMU info OIDs registered in OBP simulator at .60.{1,13,15}.0 paths. OIDs appear in registered_oids list for startup logging.</done>
</task>

<task type="auto">
  <name>Task 3: Update oidmaps.json with new OID entries and commit all changes</name>
  <files>src/SnmpCollector/config/oidmaps.json, deploy/grafana/dashboards/simetra-operations.json, deploy/grafana/dashboards/simetra-business.json</files>
  <action>
  **Part A: Add 6 new OID entries to oidmaps.json**

  Add 3 NPB static info OIDs after the existing npb_uptime entry (after line 37, before port metrics):
  ```json
  "1.3.6.1.4.1.47477.100.1.5.0": "npb_model",
  "1.3.6.1.4.1.47477.100.1.6.0": "npb_serial",
  "1.3.6.1.4.1.47477.100.1.7.0": "npb_sw_version",
  ```

  Add 3 OBP NMU static info OIDs after the existing OBP entries (after line 31, before the NPB section comment):
  ```json
  "1.3.6.1.4.1.47477.10.21.60.1.0": "obp_device_type",
  "1.3.6.1.4.1.47477.10.21.60.13.0": "obp_sw_version",
  "1.3.6.1.4.1.47477.10.21.60.15.0": "obp_serial",
  ```

  Update the comments to reflect new totals: OBP goes from "24 entries" to "27 entries", NPB goes from "68 entries" to "71 entries".

  IMPORTANT: oidmaps.json uses JSONC (JSON with comments). Keep that format. Make sure trailing commas are correct -- the last entry in the file must NOT have a trailing comma.

  **Part B: Commit everything together**

  Stage and commit all modified files in a single commit:
  - simulators/npb/npb_simulator.py
  - simulators/obp/obp_simulator.py
  - src/SnmpCollector/config/oidmaps.json
  - deploy/grafana/dashboards/simetra-operations.json (pending Host Name -> Host rename)
  - deploy/grafana/dashboards/simetra-business.json (pending service_name column hidden)

  Commit message: "fix(simulators): convert NPB health metrics to numeric types, add static info OIDs to NPB/OBP"
  </action>
  <verify>Validate oidmaps.json parses (strip comments, parse JSON). Run `git diff --cached --stat` after staging to confirm all 5 files included.</verify>
  <done>oidmaps.json contains all 6 new OID mappings. All 5 files committed together in one clean commit.</done>
</task>

</tasks>

<verification>
1. `python -c "import ast; ast.parse(open('simulators/npb/npb_simulator.py').read())"` -- NPB syntax OK
2. `python -c "import ast; ast.parse(open('simulators/obp/obp_simulator.py').read())"` -- OBP syntax OK
3. Grep oidmaps.json for "npb_model", "obp_device_type" -- all 6 new entries present
4. Grep npb_simulator.py for "Gauge32" -- system health uses numeric types
5. Grep npb_simulator.py for "OctetString" -- only appears for static info OIDs, NOT for cpu_util/mem_util/sys_temp/uptime
6. `git log -1 --oneline` -- single commit with all changes
</verification>

<success_criteria>
- NPB system health metrics use Gauge32 (cpu_util, mem_util, sys_temp) and TimeTicks (uptime) instead of OctetString
- NPB system_state stores integer values, update_system_health writes integers (x10 for percentages/temp, x100 for uptime)
- NPB has 3 static info OIDs at .100.1.{5,6,7}.0 serving OctetString values
- OBP has 3 static NMU info OIDs at .10.21.60.{1,13,15}.0 serving OctetString values
- oidmaps.json maps all 6 new OIDs to named metrics
- All changes + pending dashboard fixes committed in one commit
</success_criteria>

<output>
No SUMMARY needed for quick plans.
</output>
