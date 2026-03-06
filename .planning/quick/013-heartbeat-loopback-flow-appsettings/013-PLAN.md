---
phase: quick-013
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/appsettings.json
  - src/SnmpCollector/appsettings.Development.json
  - deploy/k8s/configmap.yaml
  - deploy/k8s/production/configmap.yaml
autonomous: true

must_haves:
  truths:
    - "Heartbeat virtual device 'heartbeat' is configured in appsettings Devices array"
    - "HeartbeatJob config section exists with IntervalSeconds default"
    - "Heartbeat OID is mapped in OidMap for metric name resolution"
    - "K8s configmaps include heartbeat device and job config"
  artifacts:
    - path: "src/SnmpCollector/appsettings.json"
      provides: "Base heartbeat config defaults"
      contains: "HeartbeatJob"
    - path: "src/SnmpCollector/appsettings.Development.json"
      provides: "Dev heartbeat device entry with loopback IP and heartbeat OID"
      contains: "heartbeat"
    - path: "deploy/k8s/configmap.yaml"
      provides: "Lab K8s heartbeat config"
      contains: "heartbeat"
    - path: "deploy/k8s/production/configmap.yaml"
      provides: "Production K8s heartbeat config with documentation"
      contains: "heartbeat"
  key_links:
    - from: "appsettings Devices[].Name"
      to: "CommunityStringHelper.DeriveFromDeviceName"
      via: "Simetra.heartbeat community string convention"
      pattern: "heartbeat"
    - from: "appsettings OidMap"
      to: "OidMapService resolution"
      via: "heartbeat OID mapped to metric name"
      pattern: "1\\.3\\.6\\.1\\.4\\.1\\.9999\\.1\\.1\\.1\\.0"
---

<objective>
Configure appsettings with the heartbeat virtual device properties needed for the self-trap heartbeat loopback flow.

Purpose: The heartbeat job (to be built later) sends a loopback SNMP trap to the local listener. The trap uses community string "Simetra.heartbeat" which the listener validates via CommunityStringHelper. While the trap path itself only needs a valid Simetra.* community string (no device registry lookup), the device entry documents the convention, the OidMap entry enables metric name resolution, and the HeartbeatJob section configures the job interval.

Output: Updated appsettings files (base, Development, K8s configmaps) with heartbeat device entry, OID mapping, and HeartbeatJob config section.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/appsettings.json
@src/SnmpCollector/appsettings.Development.json
@src/SnmpCollector/Configuration/DeviceOptions.cs
@src/SnmpCollector/Configuration/MetricPollOptions.cs
@src/SnmpCollector/Pipeline/CommunityStringHelper.cs
@src/SnmpCollector/Services/SnmpTrapListenerService.cs
@deploy/k8s/configmap.yaml
@deploy/k8s/production/configmap.yaml
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add heartbeat config to base and Development appsettings</name>
  <files>
    src/SnmpCollector/appsettings.json
    src/SnmpCollector/appsettings.Development.json
  </files>
  <action>
    **appsettings.json** (base defaults):
    1. Add `"HeartbeatJob"` section with `"IntervalSeconds": 15` (matches Simetra reference project convention). Place it after `"CorrelationJob"` section.
    2. Add `"Liveness"` section with `"GraceMultiplier": 2.0` if not already present (needed for heartbeat staleness detection). Place after `"HeartbeatJob"`.
    3. Do NOT add a heartbeat device to the base Devices array (it stays empty -- devices are environment-specific).
    4. Do NOT add OidMap entries to base (stays empty -- environment-specific).

    **appsettings.Development.json**:
    1. Add a heartbeat virtual device entry to the existing `Devices` array:
       ```json
       {
         "Name": "heartbeat",
         "IpAddress": "127.0.0.1",
         "MetricPolls": []
       }
       ```
       - Name "heartbeat" produces community string "Simetra.heartbeat" via CommunityStringHelper.DeriveFromDeviceName
       - IpAddress "127.0.0.1" documents the loopback convention (trap listener does NOT validate source IP against device IP, but it documents intent)
       - MetricPolls is empty -- the heartbeat device receives traps only, it is never polled

    2. Add the heartbeat OID to the existing `OidMap`:
       ```json
       "1.3.6.1.4.1.9999.1.1.1.0": "simetraHeartbeat"
       ```
       This is the OID from SimetraModule.HeartbeatOid in the Simetra reference project. The OidMapService will resolve this OID to "simetraHeartbeat" metric name when the trap flows through OidResolutionBehavior.

    3. Add `"HeartbeatJob"` section:
       ```json
       "HeartbeatJob": {
         "IntervalSeconds": 15
       }
       ```

    IMPORTANT: Preserve all existing device entries (npb-core-01, obp-edge-01) and OidMap entries exactly as they are. The heartbeat device is appended to the array, the OID is appended to the map.
  </action>
  <verify>
    - `cat src/SnmpCollector/appsettings.json | python -m json.tool` succeeds (valid JSON)
    - `cat src/SnmpCollector/appsettings.Development.json | python -m json.tool` succeeds (valid JSON)
    - Development appsettings contains device named "heartbeat" with IpAddress "127.0.0.1"
    - OidMap contains "1.3.6.1.4.1.9999.1.1.1.0" mapped to "simetraHeartbeat"
    - HeartbeatJob section present with IntervalSeconds 15
    - All existing config entries preserved unchanged
  </verify>
  <done>
    Base appsettings has HeartbeatJob defaults. Development appsettings has heartbeat virtual device, OID mapping, and job config. All JSON is valid.
  </done>
</task>

<task type="auto">
  <name>Task 2: Add heartbeat config to K8s configmaps</name>
  <files>
    deploy/k8s/configmap.yaml
    deploy/k8s/production/configmap.yaml
  </files>
  <action>
    **deploy/k8s/configmap.yaml** (lab environment):
    1. Add the heartbeat virtual device to the `Devices` array:
       ```json
       {
         "Name": "heartbeat",
         "IpAddress": "127.0.0.1",
         "MetricPolls": []
       }
       ```
    2. Add OidMap section if not present, with heartbeat OID:
       ```json
       "OidMap": {
         "1.3.6.1.4.1.9999.1.1.1.0": "simetraHeartbeat"
       }
       ```
    3. Add HeartbeatJob section:
       ```json
       "HeartbeatJob": {
         "IntervalSeconds": 15
       }
       ```
    4. Add Liveness section if not present:
       ```json
       "Liveness": {
         "GraceMultiplier": 2.0
       }
       ```

    **deploy/k8s/production/configmap.yaml** (production template):
    1. Add the heartbeat virtual device to the `Devices` array BEFORE the REPLACE_ME device entry. The heartbeat device is NOT a placeholder -- it is always the same in every deployment:
       ```json
       {
         "Name": "heartbeat",
         "IpAddress": "127.0.0.1",
         "MetricPolls": []
       }
       ```
    2. Add heartbeat OID to OidMap. If OidMap section doesn't exist, add it:
       ```json
       "OidMap": {
         "1.3.6.1.4.1.9999.1.1.1.0": "simetraHeartbeat"
       }
       ```
    3. The production template already has HeartbeatJob and Liveness sections -- verify they exist and leave unchanged.
    4. Add documentation comments in the YAML header for:
       - The heartbeat device (explain it is a virtual device for scheduler liveness, always present, not user-configurable)
       - The heartbeat OID mapping (explain it resolves the heartbeat trap OID to a metric name)

    IMPORTANT: The heartbeat device must appear in EVERY deployment (lab and production). It is infrastructure, not user-configured. Production template should NOT use REPLACE_ME for the heartbeat device.
  </action>
  <verify>
    - `python -c "import yaml; yaml.safe_load(open('deploy/k8s/configmap.yaml'))"` succeeds (valid YAML)
    - `python -c "import yaml; yaml.safe_load(open('deploy/k8s/production/configmap.yaml'))"` succeeds (valid YAML)
    - Both configmaps contain the heartbeat device with Name "heartbeat" and IpAddress "127.0.0.1"
    - Both configmaps contain OidMap with heartbeat OID
    - Lab configmap has HeartbeatJob and Liveness sections
    - Production template heartbeat device is NOT a REPLACE_ME placeholder
  </verify>
  <done>
    Both K8s configmaps include the heartbeat virtual device as permanent infrastructure config. Production template documents the heartbeat device as non-user-configurable.
  </done>
</task>

</tasks>

<verification>
1. All four config files parse without errors (valid JSON within valid YAML for configmaps)
2. `dotnet build src/SnmpCollector/SnmpCollector.csproj` succeeds (config changes don't break compilation)
3. Heartbeat device entry follows DeviceOptions schema: Name (string), IpAddress (string), MetricPolls (empty array)
4. Community string convention: CommunityStringHelper.DeriveFromDeviceName("heartbeat") produces "Simetra.heartbeat"
5. OID "1.3.6.1.4.1.9999.1.1.1.0" is consistent with SimetraModule.HeartbeatOid from reference project
</verification>

<success_criteria>
- All appsettings and configmap files contain heartbeat virtual device entry
- HeartbeatJob config section present with IntervalSeconds default of 15
- Heartbeat OID mapped to "simetraHeartbeat" in OidMap
- All JSON valid, build passes, existing config entries preserved
- Config is ready for the HeartbeatJob implementation (future task) to consume
</success_criteria>

<output>
After completion, create `.planning/quick/013-heartbeat-loopback-flow-appsettings/013-SUMMARY.md`
</output>
