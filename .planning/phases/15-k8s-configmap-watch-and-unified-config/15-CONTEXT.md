# Phase 15: K8s ConfigMap Watch and Unified Config - Context

**Gathered:** 2026-03-07
**Status:** Ready for planning

## Decisions

### Single ConfigMap Key
- **One JSON key** (e.g., `simetra-config.jsonc`) in the `simetra-config` ConfigMap replaces `oidmap-obp.json`, `oidmap-npb.json`, and `devices.json`
- Contains both `"OidMap"` dictionary (92 OIDs) and `"Devices"` array (OBP-01, NPB-01) in one JSON structure
- **JSONC comments** inline with each OID and device entry — documentation lives with the data
- Clear visual separation between OID map section and devices section within the file
- `appsettings.k8s.json` stays separate (app-level settings, not device config)

### K8s API Watch (Not File-Based)
- **Replace file-based hot-reload** (`IOptionsMonitor` + `FileSystemWatcher` + `reloadOnChange`) with K8s API watch
- Use `KubernetesClient` NuGet package to watch the ConfigMap resource directly
- Instant notification on ConfigMap change (no 30-60s K8s volume propagation delay)
- RBAC updated: ServiceAccount gets `configmaps` read/watch/list permission
- ConfigMap watcher parses JSON and updates both OidMap and Devices/poll config

### Full Reload Scope (OPS-01)
- **Everything reloads** on ConfigMap change: OID maps, devices, poll schedules
- Quartz poll jobs re-registered dynamically: add/remove devices, change OIDs/intervals — no pod restart
- OidMapService atomic swap stays (FrozenDictionary pattern) — just the trigger source changes from IOptionsMonitor to ConfigMap watcher
- User has explicit control: `kubectl apply` triggers reload, nothing automatic

### Local Development Fallback
- When not running in K8s (no `KUBERNETES_SERVICE_HOST` env var), fall back to file-based loading
- Single local JSON file in config directory — same format as ConfigMap key
- No hot-reload in local mode (restart to pick up changes) — keeps it simple

### Cleanup
- Delete `src/SnmpCollector/config/oidmap-obp.json` and `oidmap-npb.json` (source files with comments — superseded by ConfigMap)
- Remove multi-file auto-scan from Program.cs (`oidmap-*.json` glob loop, `devices.json` explicit load)
- Remove `reloadOnChange: true` from all `AddJsonFile` calls
- Remove `IOptionsMonitor<OidMapOptions>` dependency — replace with ConfigMap watcher callback

## Claude's Discretion

- ConfigMap watcher service architecture (IHostedService pattern, event vs callback for reload notification)
- How Quartz jobs are re-registered on device config change (reschedule vs tear-down-and-rebuild)
- Whether OidMapService keeps IOptionsMonitor interface or switches to direct injection from watcher
- K8s client configuration (in-cluster config auto-detection)
- JSONC key name (`simetra-config.jsonc` vs `simetra-devices.jsonc` vs other)
- Test strategy for ConfigMap watcher (mock K8s client vs integration test)

## Deferred Ideas

None — discussion stayed within phase scope.
