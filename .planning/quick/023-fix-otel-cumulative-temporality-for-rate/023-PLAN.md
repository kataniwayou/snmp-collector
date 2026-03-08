---
phase: quick
plan: "023"
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
autonomous: true

must_haves:
  truths:
    - "Counter metrics exported with cumulative temporality so Prometheus rate() returns non-null"
    - "Gauge metrics unaffected by temporality change"
  artifacts:
    - path: "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
      provides: "Cumulative temporality on PeriodicExportingMetricReader"
      contains: "MetricReaderTemporalityPreference.Cumulative"
  key_links:
    - from: "PeriodicExportingMetricReader"
      to: "MetricReaderTemporalityPreference.Cumulative"
      via: "constructor options"
      pattern: "TemporalityPreference.*=.*Cumulative"
---

<objective>
Fix OTel metric export to use cumulative temporality so that Prometheus rate() works on counter metrics.

Purpose: Delta temporality sends per-interval deltas (10, 5, 3) which Prometheus cannot compute rate() on.
Cumulative temporality sends monotonically increasing totals (10, 15, 18) which rate() requires.

Output: Updated ServiceCollectionExtensions.cs with cumulative temporality preference on the PeriodicExportingMetricReader.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Set cumulative temporality on PeriodicExportingMetricReader</name>
  <files>src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs</files>
  <action>
In the `AddSnmpTelemetry` method, modify the `PeriodicExportingMetricReader` constructor call (line 102) to set cumulative temporality.

Change:
```csharp
return new PeriodicExportingMetricReader(roleGated);
```

To:
```csharp
return new PeriodicExportingMetricReader(roleGated)
{
    TemporalityPreference = MetricReaderTemporalityPreference.Cumulative
};
```

This is an object initializer on the existing constructor call. `MetricReaderTemporalityPreference` is in the `OpenTelemetry.Metrics` namespace which is already imported (line 10).

Add a comment above the return statement explaining WHY cumulative is required:
```csharp
// Cumulative temporality required: Prometheus rate() needs monotonically
// increasing counter values. Delta temporality sends per-interval deltas
// which rate() cannot compute over.
```

Do NOT change the exporter interval, export processor type, or any other metric configuration.
  </action>
  <verify>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` compiles without errors
2. `dotnet build tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` compiles without errors
3. Grep the file for `TemporalityPreference.*Cumulative` confirms the setting exists
  </verify>
  <done>
PeriodicExportingMetricReader is configured with MetricReaderTemporalityPreference.Cumulative.
Build succeeds. No other telemetry configuration changed.
  </done>
</task>

</tasks>

<verification>
- `dotnet build src/SnmpCollector/SnmpCollector.csproj` succeeds
- `grep -n "TemporalityPreference" src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` shows cumulative setting
- No other changes to the file beyond the temporality preference and comment
</verification>

<success_criteria>
- PeriodicExportingMetricReader uses cumulative temporality
- Project compiles cleanly
- Change is minimal (3-4 lines added, 0 lines removed)
</success_criteria>

<output>
After completion, create `.planning/quick/023-fix-otel-cumulative-temporality-for-rate/023-SUMMARY.md`
</output>
