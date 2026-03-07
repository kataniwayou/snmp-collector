# Phase 11: OID Map Design and OBP Population - Research

**Researched:** 2026-03-07
**Domain:** .NET Configuration multi-file merge, K8s ConfigMap directory mounts, OBP SNMP OID tree design
**Confidence:** HIGH

## Summary

Phase 11 has three implementation axes: (1) multi-file OID map auto-scan in `Program.cs` with .NET configuration builder, (2) K8s deployment changes to switch from `subPath` mount to directory mount for hot-reload, and (3) authoring the OBP `oidmap-obp.json` with realistic OIDs, naming convention, and inline JSONC documentation.

The .NET `ConfigurationBuilder.AddJsonFile()` API supports `reloadOnChange: true` and JSONC comment stripping natively. Multiple JSON files containing the same `"OidMap"` section key will merge their `Entries` dictionaries automatically via the .NET configuration system's layering behavior -- later files override earlier ones for duplicate keys, but distinct keys merge. The existing `OidMapService` with `IOptionsMonitor<OidMapOptions>` requires zero code changes to support this.

The OBP device uses enterprise OID prefix `1.3.6.1.4.1.47477.10.21` with a per-link subtree at `{prefix}.{linkNum}.3.{suffix}`. Currently the simulator only implements suffix 1 (link_state) and suffix 4 (channel). Phase 11 must add optical power OIDs (R1-R4) using new suffixes within the same OID tree structure.

**Primary recommendation:** Add the auto-scan loop in `Program.cs` before `builder.Build()`, create `oidmap-obp.json` with JSONC comments as a new config file, update the K8s deployment to use directory mount, and add the new file as a key in the existing `simetra-config` ConfigMap.

## Standard Stack

### Core

No new libraries are needed. All capabilities are built into the existing .NET 9 stack.

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.Extensions.Configuration.Json` | 9.0.0 (bundled) | `AddJsonFile()` with `reloadOnChange` and JSONC support | Part of ASP.NET Core; no extra package |
| `Microsoft.Extensions.Options` | 9.0.0 (bundled) | `IOptionsMonitor<T>.OnChange` for hot-reload | Already used by `OidMapService` |

### Supporting

No additional supporting libraries required.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Directory glob in Program.cs | Custom IConfigurationSource | Over-engineered; `Directory.GetFiles` + `AddJsonFile` loop is simpler and sufficient |
| JSONC comments | Separate YAML sidecar | JSONC is natively stripped by .NET JSON parser; no tooling needed |

## Architecture Patterns

### Pattern 1: Multi-File OID Map Auto-Scan in Program.cs

**What:** Before `builder.Build()`, scan a config directory for `oidmap-*.json` files and register each with `AddJsonFile(path, optional: true, reloadOnChange: true)`.

**When to use:** Always -- this is the mechanism that enables per-device-type OID map files.

**Key implementation details:**

1. The scan must happen BEFORE `builder.Build()` because `AddJsonFile` modifies the `ConfigurationBuilder`.
2. Use `Directory.GetFiles(configDir, "oidmap-*.json")` for the glob.
3. The config directory path differs between local dev (`./`) and K8s (`/app/config/`). Use an environment variable or convention to select.
4. Each file must contain an `"OidMap"` section with entries. The .NET config system merges all sections with the same key path.
5. Set `optional: true` so the app starts even if no OID map files exist (empty map is valid).
6. Set `reloadOnChange: true` to enable hot-reload via file watcher.

**Code pattern:**

```csharp
// In Program.cs, before builder.Build()
var configDir = builder.Configuration["ConfigDirectory"] ?? builder.Environment.ContentRootPath;
if (Directory.Exists(configDir))
{
    foreach (var file in Directory.GetFiles(configDir, "oidmap-*.json").OrderBy(f => f))
    {
        builder.Configuration.AddJsonFile(file, optional: true, reloadOnChange: true);
    }
}
```

**Why `OrderBy`:** Deterministic merge order. Later files override earlier ones for duplicate OID keys. Alphabetical order means `oidmap-npb.json` overrides `oidmap-obp.json` for any shared OID (unlikely but predictable).

### Pattern 2: .NET Configuration Merge Semantics

**What:** When multiple JSON files have the same section path (e.g., `OidMap`), .NET merges their child keys. This is NOT array append -- it is dictionary merge at each level.

**Critical understanding:**

```json
// File 1: oidmap-obp.json
{
  "OidMap": {
    "1.3.6.1.4.1.47477.10.21.1.3.1.0": "obp_link_state_L1"
  }
}

// File 2: oidmap-npb.json
{
  "OidMap": {
    "1.3.6.1.4.1.47477.100.4.1.0": "npb_port_status_P1"
  }
}

// Result: OidMapOptions.Entries contains BOTH keys
```

The `OidMapOptions.Entries` dictionary binding (`config.GetSection("OidMap").Bind(opts.Entries)`) picks up all keys from the merged configuration tree. No code changes needed in `ServiceCollectionExtensions.cs` or `OidMapService.cs`.

### Pattern 3: K8s Directory Mount for Hot-Reload

**What:** Switch from `subPath` mount to directory mount so K8s automatically propagates ConfigMap changes.

**Current deployment.yaml (subPath -- blocks hot-reload):**

```yaml
volumeMounts:
- name: config
  mountPath: /app/appsettings.Production.json
  subPath: appsettings.k8s.json
  readOnly: true
```

**Target deployment.yaml (directory mount -- enables hot-reload):**

```yaml
volumeMounts:
- name: config
  mountPath: /app/config
  readOnly: true
```

**Why directory mount enables hot-reload:** K8s uses symlinks for ConfigMap directory mounts. When the ConfigMap is updated, K8s atomically swaps the symlink target (~30-60s propagation). The .NET file watcher detects this and triggers `IOptionsMonitor.OnChange`. With `subPath`, K8s mounts a single file directly (no symlink), so updates are NOT propagated.

**Impact on appsettings.Production.json:** Currently the main config file (`appsettings.k8s.json`) is mounted as `/app/appsettings.Production.json` via subPath. With directory mount, the file lives at `/app/config/appsettings.k8s.json`. Program.cs must explicitly `AddJsonFile("/app/config/appsettings.k8s.json", ...)` or scan the config directory for all JSON files.

**Recommended approach:** Mount the ConfigMap directory at `/app/config/`, then in Program.cs scan for all `*.json` files in that directory. This handles both the main config and OID map files uniformly.

### Pattern 4: OBP OID Tree Structure

**What:** The OBP device uses enterprise OID prefix `1.3.6.1.4.1.47477.10.21`.

**Existing OID tree (from simulator):**

```
1.3.6.1.4.1.47477.10.21.{linkNum}.3.{suffix}.0

Link numbers: 1, 2, 3, 4
Suffix 1 = link_state (Integer32: 0=off, 1=on)
Suffix 4 = channel   (Integer32: 0=bypass, 1=primary)
```

**New OIDs to add (optical power R1-R4):**

Following the same tree pattern, optical power readings use new suffixes. Realistic suffix allocation:

```
Suffix 1  = link_state  (existing)
Suffix 4  = channel     (existing)
Suffix 10 = r1_power    (new - Rx optical power, receiver 1)
Suffix 11 = r2_power    (new - Rx optical power, receiver 2)
Suffix 12 = r3_power    (new - Rx optical power, receiver 3)
Suffix 13 = r4_power    (new - Rx optical power, receiver 4)
```

**OID naming convention applied:**

| OID | Metric Name | SNMP Type | Units | Range |
|-----|-------------|-----------|-------|-------|
| `...{L}.3.1.0` | `obp_link_state_L{L}` | Integer32 | enum | 0=off, 1=on |
| `...{L}.3.4.0` | `obp_channel_L{L}` | Integer32 | enum | 0=bypass, 1=primary |
| `...{L}.3.10.0` | `obp_r1_power_L{L}` | Integer32 | 0.01 dBm | -4000 to 0 (-40.00 to 0.00 dBm) |
| `...{L}.3.11.0` | `obp_r2_power_L{L}` | Integer32 | 0.01 dBm | -4000 to 0 |
| `...{L}.3.12.0` | `obp_r3_power_L{L}` | Integer32 | 0.01 dBm | -4000 to 0 |
| `...{L}.3.13.0` | `obp_r4_power_L{L}` | Integer32 | 0.01 dBm | -4000 to 0 |

Total OBP OIDs: 4 links x 6 OIDs = 24 entries in `oidmap-obp.json`.

**Why Integer32 for optical power:** Enterprise MIBs commonly encode optical power as Integer32 in hundredths of dBm (0.01 dBm units) to avoid floating-point issues in SNMP. A value of -2350 means -23.50 dBm. Range -4000 to 0 covers typical fiber optic power levels (-40.00 dBm to 0.00 dBm).

### Anti-Patterns to Avoid

- **Don't nest OID map by device type in JSON:** Using `"OidMap": { "obp": { ... }, "npb": { ... } }` would require changes to `OidMapOptions` binding. The flat dictionary `"OidMap": { "oid": "name" }` works with the existing binding code.
- **Don't use `subPath` for files that need hot-reload:** K8s does not propagate ConfigMap changes to subPath-mounted files.
- **Don't register `AddJsonFile` after `builder.Build()`:** The configuration builder is sealed after `Build()`. All JSON sources must be added before.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON comment stripping | Custom pre-parser | .NET `AddJsonFile` (strips JSONC comments natively) | Built into `System.Text.Json` since .NET 6 |
| Config file change detection | Custom FileSystemWatcher | `reloadOnChange: true` on `AddJsonFile` | Handles symlink swaps, debouncing, error recovery |
| Dictionary merge from multiple files | Custom merge logic | .NET Configuration layering | The config system already merges same-key sections across files |
| OID map hot-reload notification | Custom file watcher + event | `IOptionsMonitor<T>.OnChange` | Already wired in `OidMapService` |

**Key insight:** The entire multi-file, hot-reloadable, auto-merged OID map is achievable with ZERO new infrastructure code. The .NET configuration system and existing `OidMapService` handle everything. The only new code is the glob loop in `Program.cs`.

## Common Pitfalls

### Pitfall 1: SubPath Mount Blocks Hot-Reload

**What goes wrong:** Files mounted via K8s `subPath` are never updated when the ConfigMap changes. Developers expect hot-reload but it silently never fires.
**Why it happens:** K8s uses bind mounts for subPath (no symlink), so the kubelet does not update the file.
**How to avoid:** Use directory mount (no subPath). Verify by updating ConfigMap and checking pod logs for `OidMap hot-reloaded` message.
**Warning signs:** ConfigMap updated but `OidMapService` never logs a reload event.

### Pitfall 2: Configuration Source Order Matters

**What goes wrong:** If `oidmap-*.json` files are added BEFORE `appsettings.Production.json`, the Production config could override OID map entries with an empty `"OidMap": {}` section.
**Why it happens:** .NET configuration is last-wins for duplicate keys. The order of `AddJsonFile` calls determines priority.
**How to avoid:** Add OID map files AFTER the main appsettings files. The `WebApplication.CreateBuilder` already registers `appsettings.json` and `appsettings.{Environment}.json` by default, so adding OID map files after `CreateBuilder` returns is correct.
**Warning signs:** OID map has 0 entries despite `oidmap-*.json` files existing.

### Pitfall 3: appsettings.Production.json Conflict with Directory Mount

**What goes wrong:** When switching from subPath to directory mount, the main config file path changes. If `appsettings.Production.json` is still expected at `/app/appsettings.Production.json` but the ConfigMap is now at `/app/config/appsettings.k8s.json`, the app won't find production config.
**Why it happens:** `WebApplication.CreateBuilder` auto-loads `appsettings.{Environment}.json` from `ContentRootPath` (/app/), not from the config directory.
**How to avoid:** Two options:
  1. Rename the ConfigMap key to `appsettings.Production.json` and mount directory at `/app/` (risks overwriting published files).
  2. Keep mounting at `/app/config/` and explicitly `AddJsonFile("/app/config/appsettings.k8s.json", ...)` in Program.cs.

Option 2 is safer. Add the main config file explicitly, then scan for oidmap files in the same directory.

### Pitfall 4: JSONC Comment Syntax Limitations

**What goes wrong:** Using `/* block comments */` in JSON files. .NET's JSON parser supports `//` line comments and `/* */` block comments when `JsonSerializerOptions.ReadCommentHandling` is set, but `AddJsonFile` uses its own parser which supports both.
**Why it happens:** Confusion about which JSON parser variant is used.
**How to avoid:** Use `//` line comments consistently. Both comment styles work with `AddJsonFile`, but `//` is more common in JSONC convention.
**Warning signs:** JSON parse errors in logs at startup.

### Pitfall 5: File Watcher on Linux with K8s Symlinks

**What goes wrong:** The .NET file watcher may not detect changes when K8s atomically swaps symlinks for ConfigMap updates.
**Why it happens:** `inotify` events fire on the symlink, and the .NET `PhysicalFileProvider` watches the real path.
**How to avoid:** .NET 6+ `PhysicalFileProvider` handles this correctly for K8s ConfigMap symlink swaps -- it monitors the directory and detects the atomic swap. This is a solved problem in modern .NET.
**Warning signs:** ConfigMap changes don't trigger reload in the first ~60 seconds. K8s propagation delay (kubelet sync period) is 30-60 seconds by default; this is normal, not a bug.

## Code Examples

### Example 1: Program.cs Auto-Scan Addition

```csharp
var builder = WebApplication.CreateBuilder(args);

// Auto-scan for oidmap-*.json files in config directory
// Must happen BEFORE builder.Build() -- AddJsonFile modifies ConfigurationBuilder
var configDir = Environment.GetEnvironmentVariable("CONFIG_DIRECTORY")
    ?? Path.Combine(builder.Environment.ContentRootPath, "config");

if (Directory.Exists(configDir))
{
    // Load main K8s config if present (directory mount replaces subPath)
    var k8sConfig = Path.Combine(configDir, "appsettings.k8s.json");
    if (File.Exists(k8sConfig))
    {
        builder.Configuration.AddJsonFile(k8sConfig, optional: true, reloadOnChange: true);
    }

    // Auto-scan OID map files -- alphabetical order for deterministic merge
    foreach (var file in Directory.GetFiles(configDir, "oidmap-*.json").OrderBy(f => f))
    {
        builder.Configuration.AddJsonFile(file, optional: true, reloadOnChange: true);
    }
}

// ... rest of DI registration unchanged
```

### Example 2: oidmap-obp.json Structure (JSONC with inline docs)

```jsonc
{
  "OidMap": {
    // === OBP Optical Bypass - Link 1 ===

    // link_state: Integer32, 0=off 1=on. Reports physical link presence.
    "1.3.6.1.4.1.47477.10.21.1.3.1.0": "obp_link_state_L1",

    // channel: Integer32, 0=bypass 1=primary. Current optical path selection.
    "1.3.6.1.4.1.47477.10.21.1.3.4.0": "obp_channel_L1",

    // r1_power: Integer32, units=0.01 dBm, range=-4000..0 (-40.00..0.00 dBm).
    // Receiver 1 optical input power level.
    "1.3.6.1.4.1.47477.10.21.1.3.10.0": "obp_r1_power_L1",

    // r2_power: Integer32, units=0.01 dBm, range=-4000..0.
    "1.3.6.1.4.1.47477.10.21.1.3.11.0": "obp_r2_power_L1",

    // r3_power: Integer32, units=0.01 dBm, range=-4000..0.
    "1.3.6.1.4.1.47477.10.21.1.3.12.0": "obp_r3_power_L1",

    // r4_power: Integer32, units=0.01 dBm, range=-4000..0.
    "1.3.6.1.4.1.47477.10.21.1.3.13.0": "obp_r4_power_L1"

    // ... repeat for Links 2-4 with linkNum=2,3,4
  }
}
```

### Example 3: ConfigMap with Multiple File Keys

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-config
  namespace: simetra
data:
  appsettings.k8s.json: |
    {
      "Site": { "Name": "site-lab-01" },
      "OidMap": {}
    }
  oidmap-obp.json: |
    {
      "OidMap": {
        "1.3.6.1.4.1.47477.10.21.1.3.1.0": "obp_link_state_L1"
      }
    }
  oidmap-npb.json: |
    {
      "OidMap": {
        "1.3.6.1.4.1.47477.100.4.1.0": "npb_port_status_P1"
      }
    }
```

### Example 4: K8s Deployment Directory Mount

```yaml
volumeMounts:
- name: config
  mountPath: /app/config
  readOnly: true
volumes:
- name: config
  configMap:
    name: simetra-config
```

All ConfigMap keys become files in `/app/config/`: `appsettings.k8s.json`, `oidmap-obp.json`, `oidmap-npb.json`.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Single `appsettings.json` with all OIDs | Multi-file `oidmap-*.json` per device type | Phase 11 | Adding device types requires only a ConfigMap change |
| SubPath mount for K8s config | Directory mount for hot-reload | Phase 11 | ConfigMap changes propagate to pods without restart |
| Empty `"OidMap": {}` in all environments | Populated OID maps with documentation | Phase 11 | OID resolution works for OBP devices |

## Open Questions

1. **Config directory detection in local dev vs K8s**
   - What we know: In K8s, directory mount creates `/app/config/`. In local dev, no such directory exists.
   - What's unclear: Should we create a `config/` directory in the project for local dev, or use ContentRootPath?
   - Recommendation: Use `CONFIG_DIRECTORY` env var (set in Dockerfile/deployment.yaml) with fallback to a `config/` subdirectory of ContentRootPath. For local dev, place `oidmap-*.json` files in `src/SnmpCollector/config/` and ensure they're copied to output.

2. **appsettings.k8s.json loading after directory mount switch**
   - What we know: Currently loaded via subPath as `/app/appsettings.Production.json`. After switch, it'll be at `/app/config/appsettings.k8s.json`.
   - What's unclear: Whether to keep the auto-detected `appsettings.Production.json` path or load explicitly from config dir.
   - Recommendation: Load explicitly from config directory in the auto-scan loop. The `appsettings.k8s.json` in the ConfigMap is not an "environment-specific" override -- it IS the production config. Load it as another `AddJsonFile` call from the config directory.

3. **OBP optical power OID suffix allocation**
   - What we know: Suffixes 1 and 4 are used. The prefix tree is `{enterprise}.{link}.3.{suffix}.0`.
   - What's unclear: Real CGS OBP MIBs may use different suffixes.
   - Recommendation: Use suffixes 10-13 for R1-R4 power. These are fictional but consistent with the simulator's enterprise OID space. The suffixes gap (5-9) leaves room for future OIDs without collision.

## Sources

### Primary (HIGH confidence)

- **Codebase analysis** - `OidMapService.cs`, `OidMapOptions.cs`, `ServiceCollectionExtensions.cs`, `Program.cs` -- verified existing binding and hot-reload mechanism
- **OBP simulator** - `simulators/obp/obp_simulator.py` -- verified OID prefix `1.3.6.1.4.1.47477.10.21`, link numbers 1-4, suffixes 1 and 4
- **K8s deployment** - `deploy/k8s/deployment.yaml` -- verified current subPath mount pattern
- **ConfigMap** - `deploy/k8s/configmap.yaml` -- verified single-key structure with empty OidMap

### Secondary (MEDIUM confidence)

- **.NET `AddJsonFile` JSONC support** -- .NET 6+ `System.Text.Json` supports JSONC (`//` and `/* */` comments) in configuration files. Verified via training data knowledge of Microsoft.Extensions.Configuration.Json behavior. The `JsonConfigurationFileParser` uses `JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }`.
- **K8s directory mount vs subPath hot-reload behavior** -- Well-documented K8s behavior. SubPath uses bind mount (no update propagation); directory mount uses symlinks (atomic swap on ConfigMap update, ~30-60s propagation delay).
- **.NET `PhysicalFileProvider` K8s symlink handling** -- .NET 6+ correctly handles K8s ConfigMap symlink swaps for `reloadOnChange`. Known resolved issue from earlier .NET Core versions.

### Tertiary (LOW confidence)

- **Optical power encoding convention (0.01 dBm Integer32)** -- Based on common enterprise MIB patterns for fiber optic devices. Real CGS OBP MIBs may differ. Acceptable since the simulator is fictional.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Uses only built-in .NET configuration APIs already present in the project
- Architecture: HIGH - Multi-file merge, directory mount, and auto-scan are well-understood patterns verified against codebase
- OBP OID design: MEDIUM - OID suffixes and optical power encoding are reasonable but fictional (matching the fictional enterprise OID space)
- Pitfalls: HIGH - SubPath vs directory mount, config order, and JSONC support are well-documented behaviors

**Research date:** 2026-03-07
**Valid until:** 2026-04-07 (stable domain; .NET config system rarely changes)
