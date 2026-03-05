# Quick Task 003: Verify Logging Framework

**Status:** Complete (verification only, no code changes)
**Date:** 2026-03-05

## Claim 1: Framework — OpenTelemetry with structured logging

**CONFIRMED**

| Component | Evidence |
|-----------|----------|
| OTel SDK | `ServiceCollectionExtensions.cs:73` — `builder.Services.AddOpenTelemetry()` |
| OTel logging | `ServiceCollectionExtensions.cs:120` — `builder.Logging.AddOpenTelemetry(...)` |
| OTLP exporter | `ServiceCollectionExtensions.cs:130` — `logging.AddOtlpExporter(...)` to gRPC endpoint |
| Structured logging | `ILogger` with named placeholders used throughout (e.g., `{DeviceName}`, `{JobKey}`) |
| Include scopes | `ServiceCollectionExtensions.cs:122` — `logging.IncludeScopes = true` |
| Include formatted message | `ServiceCollectionExtensions.cs:123` — `logging.IncludeFormattedMessage = true` |
| No traces | `ServiceCollectionExtensions.cs:72` — comment: "No WithTracing block (LOG-07)" |

## Claim 2: All logs include site name, role, correlationId

**CONFIRMED** — via two independent paths:

### OTLP Logs (all instances, always active)

`SnmpLogEnrichmentProcessor.cs:45-57` — `BaseProcessor<LogRecord>.OnEnd()` adds three attributes to every log record:

| Attribute | Source | Line |
|-----------|--------|------|
| `site_name` | `IOptions<SiteOptions>.Value.Name` (resolved once at construction) | :52 |
| `role` | `Func<string>` → `ILeaderElection.CurrentRole` (evaluated per-record for dynamic leadership) | :53 |
| `correlationId` | `OperationCorrelationId ?? CurrentCorrelationId` (AsyncLocal fallback to global) | :54-55 |

Wired at `ServiceCollectionExtensions.cs:134-143`.

### Console Logs (conditional, see Claim 3)

`SnmpConsoleFormatter.cs:76-95` — format: `{timestamp} [{level}] [{site}|{role}|{globalId}|{operationId}] {category} {message}`

| Field | Source | Line |
|-------|--------|------|
| `site` | `IOptions<SiteOptions>.Value.Name` | :76 |
| `role` | `ILeaderElection.CurrentRole` (fallback `SiteOptions.Role`) | :77 |
| `globalId` | `ICorrelationService.CurrentCorrelationId` | :78 |
| `operationId` | `ICorrelationService.OperationCorrelationId` (shown only when non-null) | :79, :91-95 |

## Claim 3: Console output configurable via Logging.EnableConsole (default false)

**CONFIRMED**

| Point | Evidence |
|-------|----------|
| Options class | `LoggingOptions.cs:15` — `public bool EnableConsole { get; set; }` (default `false` — C# bool default) |
| Config binding | `ServiceCollectionExtensions.cs:62-63` — `builder.Configuration.GetSection("Logging").Bind(loggingOptions)` |
| Conditional wiring | `ServiceCollectionExtensions.cs:108` — `if (loggingOptions.EnableConsole)` gates `AddConsole()` |
| Providers cleared first | `ServiceCollectionExtensions.cs:104` — `builder.Logging.ClearProviders()` removes all defaults |
| appsettings.json | `"EnableConsole": false` (production default) |
| appsettings.Development.json | `"EnableConsole": true` (dev override) |

When `EnableConsole=false`: `ClearProviders()` removes Console/Debug/EventSource, and the `if` block is skipped. Zero stdout output. OTLP export still active.

## Claim 4: Log levels controlled via Logging.LogLevel.Default (default Information)

**CONFIRMED**

| Point | Evidence |
|-------|----------|
| appsettings.json | `"Logging": { "LogLevel": { "Default": "Information" } }` |
| appsettings.Development.json | `"LogLevel": { "Default": "Debug" }` (dev override) |
| Standard .NET mechanism | `LogLevel` sub-section handled by built-in `Microsoft.Extensions.Logging` infrastructure — no custom code needed |

Note: `LoggingOptions.cs:5-6` explicitly documents: "The LogLevel sub-section is handled by the built-in .NET logging system. This class only captures the SnmpCollector-specific EnableConsole field."

## Summary

| Claim | Verdict |
|-------|---------|
| Framework: OpenTelemetry with structured logging | **CONFIRMED** |
| All logs include: site_name, role, correlationId | **CONFIRMED** (OTLP via enrichment processor, console via custom formatter) |
| Console configurable via Logging.EnableConsole | **CONFIRMED** (default false, ClearProviders + conditional AddConsole) |
| Log levels via Logging.LogLevel.Default | **CONFIRMED** (default Information, standard .NET mechanism) |
