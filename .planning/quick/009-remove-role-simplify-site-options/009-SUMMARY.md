# Quick Task 009: Remove Redundant Role, Simplify Site Options

**Status:** Complete
**Date:** 2026-03-05

## What Was Done

Removed `SiteOptions.Role` property and all references. The "standalone" Role was a Phase 1 artifact superseded by `ILeaderElection.CurrentRole` in Phase 7.

### Changes

| File | Change |
|------|--------|
| `src/SnmpCollector/Configuration/SiteOptions.cs` | Removed `Role` property and its doc comment |
| `src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs` | Removed `_siteOptions?.Value.Role` fallback — now `_leaderElection?.CurrentRole ?? "unknown"` |
| `src/SnmpCollector/appsettings.json` | Removed `"Role": "standalone"` from Site section |

### Not Changed (no references to Role)

- `SiteOptionsValidator.cs` — only validates `Name`
- `SnmpLogEnrichmentProcessor.cs` — uses `_roleProvider()` from `ILeaderElection`
- K8s ConfigMaps — never had `Role` in Site section
- Tests — all use `new SiteOptions { Name = "test-site" }` without Role

### SiteOptions After Change

```csharp
public sealed class SiteOptions
{
    public const string SectionName = "Site";
    [Required] public required string Name { get; set; }
    public string? PodIdentity { get; set; }  // PostConfigured from HOSTNAME
}
```

### Verification

- Build: 0 errors, 0 warnings
- Tests: 137/137 passed

## Commit

- `1428f21` — refactor(quick-009): remove redundant SiteOptions.Role property
