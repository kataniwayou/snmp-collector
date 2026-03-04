# Phase 1: Infrastructure Foundation - Research

**Researched:** 2026-03-05
**Domain:** .NET 9 Generic Host, OpenTelemetry SDK, structured logging, OTLP push pipeline, config validation
**Confidence:** HIGH

---

## Summary

This phase builds the complete foundation that every subsequent phase builds into: a running .NET 9
Generic Host with OTel SDK registered (metrics + logs, no traces), structured console logging with
a custom formatter, correlation ID management, OTLP gRPC push pipeline configured, and startup
config validation with fail-fast behavior. The Docker Compose local dev stack (OTel Collector +
Prometheus + Grafana) is also wired in this phase.

The reference implementation in `src/Simetra/` contains every pattern this phase needs. The primary
work is copying and adapting Simetra's telemetry infrastructure with two structural changes: (1) this
project uses a Generic Host (`Microsoft.NET.Sdk`) rather than Simetra's Web Host (`Microsoft.NET.Sdk.Web`)
because it needs no HTTP endpoints in Phase 1, and (2) the OTel Collector will use
`prometheusremotewriteexporter` for a pure push pipeline (Simetra uses a scrape-endpoint `prometheus`
exporter). Everything else — formatter, correlation service, enrichment processor, config validation
pattern, DI extension methods — copies directly from Simetra with namespace adjustments.

**Primary recommendation:** Use `Microsoft.NET.Sdk` (Generic Host, not Web Host) for Phase 1. Add
Quartz for the CorrelationJob scheduler. Namespace everything under `SnmpCollector.*`. Copy Simetra's
telemetry files verbatim, then modify.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.NET.Sdk` (Generic Host) | .NET 9 | Application host, DI, configuration, IHostedService | Phase 1 needs no HTTP; Generic Host is simpler than Web Host for daemons |
| `OpenTelemetry.Extensions.Hosting` | 1.15.0 | `AddOpenTelemetry()`, MeterProvider lifecycle with host | Required to wire OTel SDK into Generic Host startup/shutdown |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.0 | OTLP gRPC export for metrics and logs | Implements `AddOtlpExporter()` for both `WithMetrics` and `AddOpenTelemetry` logging |
| `Quartz.Extensions.Hosting` | 3.15.1 | `AddQuartz()` and `AddQuartzHostedService()` | CorrelationJob requires a scheduler; matches Simetra's version |

**Important: use `Quartz.Extensions.Hosting` not `Quartz.AspNetCore`.** Simetra uses `Quartz.AspNetCore`
because it is a Web Host. The new project is a Generic Host — use the non-Web hosting integration.

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `OpenTelemetry.Instrumentation.Runtime` | 1.15.0 | Automatic GC, memory, thread pool metrics | Register in Phase 1 so baseline runtime metrics flow through the pipeline immediately |
| `Microsoft.Extensions.Hosting` | (in-box, .NET 9) | `IHostedService`, `IHost`, `HostBuilder` | Core host infrastructure, no explicit package needed with SDK |
| `Microsoft.Extensions.Options` | (in-box, .NET 9) | `IOptions<T>`, `ValidateOnStart`, `ValidateDataAnnotations` | Config binding and fail-fast validation |
| `Microsoft.Extensions.Logging` | (in-box, .NET 9) | `ILogger<T>`, `AddConsole`, `ClearProviders` | Logging infrastructure; OTel bridges ILogger to OTLP logs |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `Microsoft.NET.Sdk` (Generic Host) | `Microsoft.NET.Sdk.Web` (Web Host) | Web Host adds Kestrel, ASP.NET middleware — unnecessary overhead for Phase 1. Phase 8 adds health probes; that is the point to switch to Web Host if health endpoints are needed. The CONTEXT.md does not require health probes in Phase 1. |
| `Quartz.Extensions.Hosting` | `PeriodicTimer` (BCL) | `PeriodicTimer` would work for one job but does not give `[DisallowConcurrentExecution]`, misfire handling, or the full Quartz ecosystem needed in later phases. Use Quartz from Phase 1 to match Simetra's pattern. |
| `prometheusremotewriteexporter` | `prometheus` exporter (scrape) | Simetra uses `prometheus` (scrape-endpoint). This project's requirements explicitly specify `prometheusremotewriteexporter` (PUSH-01, PUSH-02, PUSH-03). Do not use scrape; it contradicts PUSH-03 (no scrape endpoint in the application OR the collector). |

### Installation

```bash
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.15.0
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.15.0
dotnet add package OpenTelemetry.Instrumentation.Runtime --version 1.15.0
dotnet add package Quartz.Extensions.Hosting --version 3.15.1
```

---

## Architecture Patterns

### Recommended Project Structure

The new project should follow Simetra's directory layout with adjusted namespace and no Devices/Pipeline/Services directories in Phase 1:

```
src/SnmpCollector/
├── Configuration/
│   ├── CorrelationJobOptions.cs        # [Range(1, int.MaxValue)] IntervalSeconds = 30
│   ├── LoggingOptions.cs               # EnableConsole bool
│   ├── OtlpOptions.cs                  # Endpoint + ServiceName, both [Required]
│   ├── SiteOptions.cs                  # Name [Required], Role optional (default "standalone")
│   └── Validators/
│       ├── OtlpOptionsValidator.cs     # IValidateOptions<OtlpOptions>
│       └── SiteOptionsValidator.cs     # IValidateOptions<SiteOptions>
├── Extensions/
│   └── ServiceCollectionExtensions.cs  # AddSnmpLogging(), AddSnmpTelemetry(), AddSnmpConfiguration()
├── Jobs/
│   └── CorrelationJob.cs               # IJob, [DisallowConcurrentExecution]
├── Pipeline/
│   ├── ICorrelationService.cs
│   └── RotatingCorrelationService.cs   # volatile string + AsyncLocal
├── Telemetry/
│   ├── SnmpConsoleFormatter.cs         # ConsoleFormatter subclass
│   ├── SnmpLogEnrichmentProcessor.cs   # BaseProcessor<LogRecord>
│   └── TelemetryConstants.cs           # Meter names
├── GlobalUsings.cs
├── Program.cs
├── SnmpCollector.csproj
├── appsettings.json                    # Full skeleton with all sections
├── appsettings.Development.json        # EnableConsole: true, localhost OTLP endpoint
└── appsettings.Production.json         # Production overrides (empty or minimal)
```

```
deploy/
├── docker-compose.yml                   # OTel Collector + Prometheus + Grafana
├── otel-collector-config.yaml           # prometheusremotewriteexporter pipeline
├── prometheus.yml                       # Scrapes nothing (pure remote_write target)
└── grafana/
    └── provisioning/
        └── datasources/
            └── prometheus.yaml          # Auto-provisioned datasource
```

### Pattern 1: Generic Host Entry Point

Phase 1 uses `Host.CreateApplicationBuilder` (or `Host.CreateDefaultBuilder`), not `WebApplication.CreateBuilder`. Simetra's `Program.cs` uses Web Host because it maps health check endpoints. Phase 1 has no HTTP endpoints.

```csharp
// Program.cs — Generic Host (not WebApplication)
var builder = Host.CreateApplicationBuilder(args);

// DI registration order matters: telemetry registered FIRST (disposed LAST = ForceFlush runs at shutdown)
builder.AddSnmpTelemetry();
builder.Services.AddSnmpConfiguration(builder.Configuration);
builder.Services.AddSnmpScheduling(builder.Configuration);

var host = builder.Build();

// Seed first correlation ID before any job fires (matches Simetra's pattern exactly)
var correlationService = host.Services.GetRequiredService<ICorrelationService>();
correlationService.SetCorrelationId(Guid.NewGuid().ToString("N"));

try
{
    await host.RunAsync();
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine("Configuration validation failed:");
    foreach (var failure in ex.Failures)
        Console.Error.WriteLine($"  - {failure}");
    throw;
}
```

### Pattern 2: AddSnmpTelemetry Extension Method

Copy Simetra's `AddSimetraTelemetry` and strip leader election and trace registration. Phase 1 (and
this entire project) has no Kubernetes leader election in scope for Phase 1, and no traces (LOG-07).

**Key differences from Simetra:**
- Remove `WithTracing(...)` block entirely (LOG-07: no traces)
- Remove K8s leader election — Phase 1 has no `ILeaderElection` concept; use a constant role string (`"standalone"`) in the formatter and enrichment processor
- No `MetricRoleGatedExporter` — add `AddOtlpExporter()` directly (role gating is Phase 7)
- The `IHostApplicationBuilder` extension signature works for both `HostApplicationBuilder` (Generic Host) and `WebApplicationBuilder` (Web Host) — keep the same signature

```csharp
// Extensions/ServiceCollectionExtensions.cs
public static IHostApplicationBuilder AddSnmpTelemetry(
    this IHostApplicationBuilder builder)
{
    var otlpOptions = new OtlpOptions { Endpoint = "", ServiceName = "" };
    builder.Configuration.GetSection(OtlpOptions.SectionName).Bind(otlpOptions);

    var loggingOptions = new LoggingOptions();
    builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);

    // --- Metrics (no traces per LOG-07) ---
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(
                serviceName: otlpOptions.ServiceName,
                serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME")
                    ?? Environment.MachineName))
        .WithMetrics(metrics =>
        {
            metrics.AddMeter(TelemetryConstants.MeterName);
            metrics.AddRuntimeInstrumentation();
            metrics.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpOptions.Endpoint);
            });
        });
    // NOTE: No .WithTracing() — LOG-07 explicitly excludes traces

    // --- Logging ---
    builder.Logging.ClearProviders();

    if (loggingOptions.EnableConsole)
    {
        builder.Logging.AddConsole(options =>
            options.FormatterName = SnmpConsoleFormatter.FormatterName);
        builder.Logging.AddConsoleFormatter<SnmpConsoleFormatter, SnmpConsoleFormatterOptions>();

        builder.Services.AddSingleton<IPostConfigureOptions<SnmpConsoleFormatterOptions>>(sp =>
            new PostConfigureSnmpFormatterOptions(sp));
    }

    // OTLP log exporter always active
    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.IncludeScopes = true;
        logging.IncludeFormattedMessage = true;
        logging.SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService(
                    serviceName: otlpOptions.ServiceName,
                    serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME")
                        ?? Environment.MachineName));
        logging.AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(otlpOptions.Endpoint);
        });
        logging.AddProcessor(sp =>
        {
            var siteOptions = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
            var correlationService = sp.GetRequiredService<ICorrelationService>();
            return new SnmpLogEnrichmentProcessor(
                correlationService,
                siteOptions.Name,
                siteOptions.Role);  // static role string in Phase 1
        });
    });

    return builder;
}
```

### Pattern 3: RotatingCorrelationService (lock-free, copy verbatim)

Simetra's implementation is the reference. Copy it exactly. The `volatile string` + `AsyncLocal<string?>` pattern is correct and safe for single-writer/multiple-reader use.

```csharp
// Pipeline/RotatingCorrelationService.cs — copy verbatim from Simetra, adjust namespace
public sealed class RotatingCorrelationService : ICorrelationService
{
    private volatile string _correlationId = string.Empty;
    private static readonly AsyncLocal<string?> _operationCorrelationId = new();

    public string CurrentCorrelationId => _correlationId;

    public string? OperationCorrelationId
    {
        get => _operationCorrelationId.Value;
        set => _operationCorrelationId.Value = value;
    }

    public void SetCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
    }
}
```

**Why `static` on `_operationCorrelationId`:** `AsyncLocal<T>` must be `static` to be shared across all instances and flow correctly through the async execution context. If it is an instance field, each service instance has its own `AsyncLocal` slot that does not propagate. Simetra gets this right; do not change it.

### Pattern 4: Console Formatter — Dual Correlation ID Display

The CONTEXT.md specifies the console output should show BOTH global and operation IDs:
`[site-nyc-01|leader|a3f7b2c1|op-9d8e7f6a]`

Simetra's `SimetraConsoleFormatter` currently shows either `OperationCorrelationId` OR `CurrentCorrelationId` (whichever is non-null). For this project, the format shows both simultaneously.

```csharp
// Telemetry/SnmpConsoleFormatter.cs — key difference from Simetra
var globalId = _correlationService?.CurrentCorrelationId ?? "none";
var operationId = _correlationService?.OperationCorrelationId;

textWriter.Write(" [");
textWriter.Write(site);
textWriter.Write('|');
textWriter.Write(role);
textWriter.Write('|');
textWriter.Write(globalId);
if (operationId is not null)
{
    textWriter.Write('|');
    textWriter.Write(operationId);
}
textWriter.Write("] ");
```

The operation ID appears only when set (per-operation scope is optional — the global ID always shows).

**Timestamp format (Claude's discretion):** Use ISO 8601 without milliseconds: `yyyy-MM-ddTHH:mm:ssZ`.
This matches Simetra exactly and is readable in `kubectl logs`. Log level abbreviations: use
Simetra's 3-character style: TRC/DBG/INF/WRN/ERR/CRT.

### Pattern 5: Config Validation (Progressive)

Phase 1 validates only Site + Otlp + Logging + CorrelationJob. Later phases add their own sections.

```csharp
// Extensions/ServiceCollectionExtensions.cs
public static IServiceCollection AddSnmpConfiguration(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Phase 1 sections — ValidateOnStart for fail-fast
    services.AddOptions<SiteOptions>()
        .Bind(configuration.GetSection(SiteOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddOptions<OtlpOptions>()
        .Bind(configuration.GetSection(OtlpOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddOptions<LoggingOptions>()
        .Bind(configuration.GetSection(LoggingOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddOptions<CorrelationJobOptions>()
        .Bind(configuration.GetSection(CorrelationJobOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // Custom IValidateOptions for cross-field validation
    services.AddSingleton<IValidateOptions<SiteOptions>, SiteOptionsValidator>();
    services.AddSingleton<IValidateOptions<OtlpOptions>, OtlpOptionsValidator>();

    return services;
}
```

### Pattern 6: OTel Collector Config (prometheusremotewriteexporter)

**This differs from Simetra.** Simetra's collector uses the `prometheus` exporter (exposes :8889 scrape
endpoint). This project requires `prometheusremotewriteexporter` — the collector pushes to Prometheus
via remote_write, and Prometheus has no scrape target for the application or collector.

The `prometheusremotewriteexporter` is available in `otelcol-contrib` (the contrib distribution), not
`otelcol` (core). Use the contrib image.

```yaml
# deploy/otel-collector-config.yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317

exporters:
  prometheusremotewrite:
    endpoint: "http://prometheus:9090/api/v1/write"
    resource_to_telemetry_conversion:
      enabled: true

service:
  pipelines:
    metrics:
      receivers: [otlp]
      exporters: [prometheusremotewrite]
    logs:
      receivers: [otlp]
      exporters: []   # No log backend in Phase 1; add later
```

**Note on logs pipeline:** Phase 1 has no log backend (no Elasticsearch, no Loki). The OTLP log
exporter in the app still pushes logs to the collector. The collector should accept them — configure
an empty or debug exporter for logs to avoid errors. Alternatively, omit the logs pipeline from the
collector entirely (the app's OTLP log export will fail silently or retry). Cleaner to configure a
`debug` exporter for logs in Phase 1 to confirm the pipeline is live.

```yaml
# Simplified: logs to debug exporter for Phase 1
exporters:
  prometheusremotewrite:
    endpoint: "http://prometheus:9090/api/v1/write"
    resource_to_telemetry_conversion:
      enabled: true
  debug:
    verbosity: basic   # Prints log records to collector stdout

service:
  pipelines:
    metrics:
      receivers: [otlp]
      exporters: [prometheusremotewrite]
    logs:
      receivers: [otlp]
      exporters: [debug]
```

### Pattern 7: Docker Compose Local Stack

```yaml
# deploy/docker-compose.yml
services:
  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.120.0
    volumes:
      - ./otel-collector-config.yaml:/etc/otelcol-contrib/config.yaml:ro
    ports:
      - "4317:4317"   # OTLP gRPC

  prometheus:
    image: prom/prometheus:v3.2.1
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml:ro
    ports:
      - "9090:9090"
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--web.enable-remote-write-receiver'   # REQUIRED for remote_write to work

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    volumes:
      - ./grafana/provisioning:/etc/grafana/provisioning:ro
    environment:
      - GF_AUTH_ANONYMOUS_ENABLED=true
      - GF_AUTH_ANONYMOUS_ORG_ROLE=Admin
```

**Critical:** Prometheus must be started with `--web.enable-remote-write-receiver` to accept remote_write
pushes from the OTel Collector. Without this flag, the `prometheusremotewriteexporter` will receive
HTTP 405 Method Not Allowed responses and no metrics will land in Prometheus.

```yaml
# deploy/prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s
# No scrape_configs needed — pure remote_write target
```

### Pattern 8: appsettings Skeleton

Full skeleton in Phase 1 with placeholder values for sections not yet active:

```json
// appsettings.json
{
  "Site": {
    "Name": "site-nyc-01",
    "Role": "standalone"
  },
  "Otlp": {
    "Endpoint": "http://otel-collector:4317",
    "ServiceName": "snmp-collector"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    },
    "EnableConsole": false
  },
  "CorrelationJob": {
    "IntervalSeconds": 30
  },
  "Devices": [],
  "OidMap": {},
  "SnmpListener": {
    "BindAddress": "0.0.0.0",
    "Port": 162,
    "CommunityString": "public"
  }
}
```

```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    },
    "EnableConsole": true
  },
  "Otlp": {
    "Endpoint": "http://localhost:4317"
  }
}
```

### Pattern 9: CorrelationJob (Quartz, copy from Simetra minus liveness)

Simetra's `CorrelationJob` calls `_liveness.Stamp(jobKey)` — that is a Phase 8 concern (health probes).
Phase 1's version omits liveness stamping but keeps the core rotation logic.

```csharp
// Jobs/CorrelationJob.cs
[DisallowConcurrentExecution]
public sealed class CorrelationJob : IJob
{
    private readonly ICorrelationService _correlation;
    private readonly ILogger<CorrelationJob> _logger;

    public CorrelationJob(ICorrelationService correlation, ILogger<CorrelationJob> logger)
    {
        _correlation = correlation;
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        try
        {
            var newCorrelationId = Guid.NewGuid().ToString("N");
            _correlation.SetCorrelationId(newCorrelationId);
            _logger.LogInformation("Correlation ID rotated to {CorrelationId}", newCorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationJob failed");
        }

        return Task.CompletedTask;
    }
}
```

### Pattern 10: AddSnmpScheduling Extension

Phase 1 scheduling is minimal — one job, one trigger.

```csharp
public static IServiceCollection AddSnmpScheduling(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddSingleton<ICorrelationService, RotatingCorrelationService>();

    var correlationOptions = new CorrelationJobOptions();
    configuration.GetSection(CorrelationJobOptions.SectionName).Bind(correlationOptions);

    services.AddQuartz(q =>
    {
        q.UseInMemoryStore();

        var correlationKey = new JobKey("correlation");
        q.AddJob<CorrelationJob>(j => j.WithIdentity(correlationKey));
        q.AddTrigger(t => t
            .ForJob(correlationKey)
            .WithIdentity("correlation-trigger")
            .StartNow()
            .WithSimpleSchedule(s => s
                .WithIntervalInSeconds(correlationOptions.IntervalSeconds)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithRemainingCount()));
    });

    services.AddQuartzHostedService(options =>
    {
        options.WaitForJobsToComplete = true;
    });

    return services;
}
```

### Anti-Patterns to Avoid

- **Using `Microsoft.NET.Sdk.Web` for Phase 1:** Adds Kestrel, middleware pipeline, and routing
  overhead for a host that serves no HTTP requests. Switch to Web Host only when health probes are
  added (Phase 8). If the team prefers starting with Web Host to avoid a later SDK switch, this is
  acceptable but the CONTEXT does not require it in Phase 1.

- **Registering `ICorrelationService` before Quartz:** The scheduler resolves `CorrelationJob` via
  DI. `ICorrelationService` must be registered as a singleton before `AddQuartz` is called.
  Simetra registers it in `AddSnmpPipeline`; in Phase 1 register it in `AddSnmpScheduling` since
  there is no pipeline yet.

- **Constructing `OtlpOptions` / `LoggingOptions` via `IOptions<T>` inside `AddSnmpTelemetry`:**
  `AddOpenTelemetry` and `builder.Logging` configuration runs during host build — `IOptions<T>` is not
  yet resolvable. Follow Simetra's pattern: bind directly from `builder.Configuration` using `.Bind()`
  on a plain `new OtlpOptions()` instance. The `ValidateOnStart` options in `AddSnmpConfiguration`
  then validate the same values at host start.

- **Putting `_operationCorrelationId` as an instance field in `RotatingCorrelationService`:**
  `AsyncLocal<T>` as an instance field means each service instance has an isolated slot — the value
  does not flow through the async execution context the way a `static` field does. Always `static`.

- **Forgetting `--web.enable-remote-write-receiver` on Prometheus Docker command:** Without this flag,
  Prometheus rejects all remote_write pushes. This is the most likely reason for a working OTel
  pipeline that produces no data in Prometheus.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Async execution context propagation for correlation IDs | Custom thread-local or `ConcurrentDictionary<int, string>` | `AsyncLocal<T>` (BCL) | `AsyncLocal` propagates through `await`, task continuations, and `Task.Run` correctly; thread-local does not |
| Console log formatting with site/role context | Custom `ILoggerProvider` | `ConsoleFormatter` subclass + `AddConsoleFormatter<T>()` | The `ConsoleFormatter` API is the correct extension point; custom providers duplicate work and bypass the formatter pipeline |
| OTel log enrichment (site_name, role, correlationId attributes) | Middleware or custom ILogger | `BaseProcessor<LogRecord>` + `logging.AddProcessor()` | OTel's processor is the correct hook; it runs after the SDK collects the record but before export, giving access to `LogRecord.Attributes` |
| Config validation fail-fast | Custom `IStartupFilter` or manual checks in `Main` | `ValidateDataAnnotations()` + `ValidateOnStart()` | `ValidateOnStart` triggers before the host starts accepting connections; catches all option errors in one place |
| Scheduled correlation rotation | `Timer` or `PeriodicTimer` in `IHostedService` | Quartz `IJob` with `[DisallowConcurrentExecution]` | Quartz handles misfire, concurrency, and the scheduler is already a dependency; a custom timer is redundant |

**Key insight:** The .NET SDK and OTel SDK provide all the extension points needed. The project adds
behavior at the correct extension points (ConsoleFormatter, BaseProcessor, ValidateOnStart) rather
than replacing framework infrastructure.

---

## Common Pitfalls

### Pitfall 1: OTel SDK Reads Config Before ValidateOnStart Runs

**What goes wrong:** `AddSnmpTelemetry()` calls `.Bind()` on `OtlpOptions` directly from
`builder.Configuration` to get the endpoint for `AddOtlpExporter()`. If the endpoint is missing or
blank, `new Uri("")` throws `UriFormatException` during host build — before `ValidateOnStart` has a
chance to produce a clear error message.

**Why it happens:** OTel SDK registration happens during DI setup. `ValidateOnStart` runs when the
host starts (after `builder.Build()`). There is a gap.

**How to avoid:** Guard the `new Uri(otlpOptions.Endpoint)` call. If the endpoint is blank, pass a
placeholder URI (e.g., `http://localhost:4317`) during registration — the `ValidateOnStart` validator
will fail-fast with the clear error. Alternatively, check for null/blank and throw `InvalidOperationException`
with a message that mirrors what the validator would say. Simetra handles this by keeping Endpoint as
`required string` and trusting that `appsettings.Development.json` always provides a valid default.
For Phase 1, the safe approach is to ensure `appsettings.Development.json` always has a valid endpoint.

**Warning signs:** `UriFormatException` in the host build log before any user-readable validation messages.

### Pitfall 2: OTel Packages Must All Be the Same Version

**What goes wrong:** Mixing `OpenTelemetry.Extensions.Hosting 1.15.0` with `OpenTelemetry.Exporter.OpenTelemetryProtocol 1.14.0` causes runtime errors because the packages share internal types that are version-coupled.

**How to avoid:** Pin all OTel packages to exactly `1.15.0`. If NuGet resolves a different version for any package, add an explicit `<PackageReference>` override. This is confirmed in the existing `STACK.md` research.

### Pitfall 3: prometheusremotewriteexporter Requires otelcol-contrib Image

**What goes wrong:** Using `otel/opentelemetry-collector:0.120.0` (core) instead of
`otel/opentelemetry-collector-contrib:0.120.0` (contrib) causes the collector to reject the config
because `prometheusremotewriteexporter` is a contrib exporter, not part of the core distribution.

**How to avoid:** Always use the `otelcol-contrib` image. Confirmed in Simetra's production
`otel-collector.yaml` which uses `otel/opentelemetry-collector-contrib:0.120.0`.

### Pitfall 4: ConsoleFormatter Cannot Use Constructor Injection for DI Services

**What goes wrong:** `ConsoleFormatter` instances are created by the framework during logging provider
setup — before the full DI container is built. Attempting to inject `ICorrelationService` or `IOptions<SiteOptions>`
via the constructor throws `InvalidOperationException` (service not found).

**Why it happens:** `ILoggerProvider` instances are created early in the logging pipeline, before
other DI services are available.

**How to avoid:** Simetra solves this with `PostConfigureSimetraFormatterOptions` — an
`IPostConfigureOptions<SnmpConsoleFormatterOptions>` that injects `IServiceProvider` into the options
object after the container is built. The formatter then lazily resolves `ICorrelationService` on its
first write. Copy this pattern verbatim.

### Pitfall 5: First Correlation ID Must Be Set Before Any Job Fires

**What goes wrong:** If the host starts Quartz jobs before the first correlation ID is seeded (via
`correlationService.SetCorrelationId(Guid.NewGuid().ToString("N"))`), the first log lines from any
job show `correlationId = ""` (the empty string default). The `CorrelationJob` itself will set the
first ID on its first fire, but there is a race between host start and job first fire.

**How to avoid:** Set the first correlation ID immediately after `builder.Build()` / `host.Build()`,
before `RunAsync()`. This is exactly what Simetra's `Program.cs` does on line 31. The CorrelationJob
then takes over at its configured interval.

### Pitfall 6: Quartz.Extensions.Hosting vs Quartz.AspNetCore

**What goes wrong:** Referencing `Quartz.AspNetCore` in a Generic Host project causes a compile error
or runtime exception because `Quartz.AspNetCore` provides `AddQuartzServer()` which is an ASP.NET Core
extension, not a generic host extension.

**How to avoid:** Use `Quartz.Extensions.Hosting` package (provides `AddQuartzHostedService()`) for
Generic Host projects. Use `Quartz.AspNetCore` only when using `WebApplication.CreateBuilder`.

### Pitfall 7: Role String in Phase 1 vs Dynamic Leader Election

**What goes wrong:** Simetra's formatter and enrichment processor resolve `ILeaderElection.CurrentRole`
dynamically. Phase 1 has no leader election. If you wire the formatter to call a non-existent service,
it crashes.

**How to avoid:** Phase 1 uses a static role string from `SiteOptions.Role` (default `"standalone"`).
The `SnmpLogEnrichmentProcessor` constructor takes `string role` directly instead of `Func<string>`.
When Phase 7 adds leader election, upgrade `SiteOptions.Role` to a `Func<string>` delegate that reads
from `ILeaderElection.CurrentRole`.

---

## Code Examples

### SiteOptions with Role Field

```csharp
// Configuration/SiteOptions.cs
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    [Required]
    public required string Name { get; set; }

    public string Role { get; set; } = "standalone";  // Phase 1 default; Phase 7 promotes to dynamic
}
```

### OtlpOptions (identical to Simetra)

```csharp
// Configuration/OtlpOptions.cs
public sealed class OtlpOptions
{
    public const string SectionName = "Otlp";

    [Required]
    public required string Endpoint { get; set; }

    [Required]
    public required string ServiceName { get; set; } = "snmp-collector";
}
```

### CorrelationJobOptions (identical to Simetra)

```csharp
// Configuration/CorrelationJobOptions.cs
public sealed class CorrelationJobOptions
{
    public const string SectionName = "CorrelationJob";

    [Range(1, int.MaxValue)]
    public int IntervalSeconds { get; set; } = 30;
}
```

### TelemetryConstants (simplified for Phase 1)

```csharp
// Telemetry/TelemetryConstants.cs
public static class TelemetryConstants
{
    /// <summary>
    /// Primary meter for all SNMP collector metrics.
    /// Phase 7 will split into leader-gated and instance meters if needed.
    /// </summary>
    public const string MeterName = "SnmpCollector";
}
```

### LoggingOptions

```csharp
// Configuration/LoggingOptions.cs
public sealed class LoggingOptions
{
    public const string SectionName = "Logging";

    public bool EnableConsole { get; set; }
}
```

### PostConfigureSnmpFormatterOptions (lazy DI injection for formatter)

```csharp
// Telemetry/SnmpConsoleFormatter.cs (inner class or same file)
internal sealed class PostConfigureSnmpFormatterOptions
    : IPostConfigureOptions<SnmpConsoleFormatterOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public PostConfigureSnmpFormatterOptions(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public void PostConfigure(string? name, SnmpConsoleFormatterOptions options)
        => options.ServiceProvider = _serviceProvider;
}
```

### SnmpLogEnrichmentProcessor (simplified — static role in Phase 1)

```csharp
// Telemetry/SnmpLogEnrichmentProcessor.cs
public sealed class SnmpLogEnrichmentProcessor : BaseProcessor<LogRecord>
{
    private readonly ICorrelationService _correlationService;
    private readonly string _siteName;
    private readonly string _role;

    public SnmpLogEnrichmentProcessor(
        ICorrelationService correlationService,
        string siteName,
        string role)
    {
        _correlationService = correlationService;
        _siteName = siteName;
        _role = role;
    }

    public override void OnEnd(LogRecord data)
    {
        var attributes = data.Attributes?.ToList()
            ?? new List<KeyValuePair<string, object?>>(3);

        attributes.Add(new KeyValuePair<string, object?>("site_name", _siteName));
        attributes.Add(new KeyValuePair<string, object?>("role", _role));
        attributes.Add(new KeyValuePair<string, object?>("correlationId",
            _correlationService.OperationCorrelationId ?? _correlationService.CurrentCorrelationId));

        data.Attributes = attributes;
    }
}
```

### Project File (SnmpCollector.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SnmpCollector</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.0" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.0" />
    <PackageReference Include="Quartz.Extensions.Hosting" Version="3.15.1" />
  </ItemGroup>

</Project>
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `HostBuilder.ConfigureLogging()` | `builder.Logging.ClearProviders()` + `builder.Logging.AddConsoleFormatter<T>()` | .NET 6+ (minimal hosting APIs) | Simpler, works identically for both Generic Host and Web Host |
| `Grpc.Core` (legacy gRPC) | `Grpc.Net.Client` (in-box) | OTel 1.10+ removed `Grpc.Core` dependency | OTel 1.15.0 uses `Grpc.Net.Client` transitively; do not add `Grpc.Core` |
| `IValidateOptions<T>` custom validator only | `ValidateDataAnnotations()` + `ValidateOnStart()` + custom `IValidateOptions<T>` | .NET 6+ Options validation | Use data annotations for simple field rules; custom validators for cross-field or logic-based rules |
| `prometheus` exporter (scrape endpoint) | `prometheusremotewriteexporter` (push) | OTel Collector contrib since early 2022 | Eliminates the scrape endpoint from the collector; Prometheus becomes a pure remote_write target |

**Deprecated/outdated:**
- `Grpc.Core`: Removed from OTel transitive dependencies in 1.10+. Do not add it.
- `WebApplication.CreateBuilder` for daemon processes: Correct for HTTP servers; use `Host.CreateApplicationBuilder` for background daemons with no HTTP endpoint in Phase 1.

---

## Open Questions

1. **Generic Host vs Web Host for Phase 1**
   - What we know: Phase 1 success criteria mention no HTTP endpoints; CONTEXT.md specifies no health probes in Phase 1.
   - What's unclear: Whether the team prefers starting with Web Host from Phase 1 to avoid changing SDK later, or wants the clean Generic Host for now.
   - Recommendation: Use Generic Host (`Microsoft.NET.Sdk`) for Phase 1. If this project follows Simetra's pattern of eventually adding health probes, switch the SDK to `Microsoft.NET.Sdk.Web` in the phase that introduces health endpoints (Phase 8 per ROADMAP).

2. **Prometheus remote_write receiver flag**
   - What we know: `--web.enable-remote-write-receiver` is required for Prometheus to accept remote_write pushes. Standard Prometheus Docker images support this flag as of v2.25+.
   - What's unclear: Whether the `prom/prometheus:v3.2.1` image enables this by default or requires the flag.
   - Recommendation: Always include the flag explicitly in docker-compose. The flag was not default as of Prometheus 3.x.

3. **appsettings.Production.json content for Phase 1**
   - What we know: CONTEXT.md specifies base + Development + Production files.
   - What's unclear: Whether Production.json should be an empty override or carry production-specific defaults.
   - Recommendation: Create an empty `appsettings.Production.json` (`{}`) as a placeholder. Kubernetes deployments will override via ConfigMap. The environment detection (`ASPNETCORE_ENVIRONMENT=Production` or `DOTNET_ENVIRONMENT=Production`) ensures the right file is loaded.

---

## Simetra Adaptation Summary

This table captures what to copy vs what to change:

| Simetra File | Action | Change Needed |
|---|---|---|
| `Pipeline/ICorrelationService.cs` | Copy verbatim | Adjust namespace to `SnmpCollector.Pipeline` |
| `Pipeline/RotatingCorrelationService.cs` | Copy verbatim | Adjust namespace only |
| `Telemetry/SimetraConsoleFormatter.cs` | Copy, modify | (1) Rename class; (2) Show both globalId and operationId in format string; (3) Remove `ILeaderElection` dependency — resolve role from `SiteOptions.Role` string |
| `Telemetry/SimetraLogEnrichmentProcessor.cs` | Copy, modify | Replace `Func<string> roleProvider` with `string role` (static in Phase 1) |
| `Telemetry/TelemetryConstants.cs` | Copy, simplify | One meter name `"SnmpCollector"` instead of `Simetra.Leader` / `Simetra.Instance` split |
| `Configuration/CorrelationJobOptions.cs` | Copy verbatim | Adjust namespace |
| `Configuration/OtlpOptions.cs` | Copy verbatim | Adjust namespace, change default ServiceName |
| `Configuration/LoggingOptions.cs` | Copy verbatim | Adjust namespace |
| `Configuration/SiteOptions.cs` | Copy, add field | Add `Role` string property with default `"standalone"` |
| `Configuration/Validators/OtlpOptionsValidator.cs` | Copy verbatim | Adjust namespace |
| `Configuration/Validators/SiteOptionsValidator.cs` | Copy verbatim | Adjust namespace |
| `Jobs/CorrelationJob.cs` | Copy, simplify | Remove `ILivenessVectorService` dependency and `_liveness.Stamp()` call |
| `Extensions/ServiceCollectionExtensions.cs` | Copy, heavily modify | (1) Remove leader election block; (2) Remove `WithTracing` block; (3) Remove role-gated exporters; (4) Split into `AddSnmpTelemetry`, `AddSnmpConfiguration`, `AddSnmpScheduling` |
| `Program.cs` | Rewrite | Use `Host.CreateApplicationBuilder`, not `WebApplication.CreateBuilder`; no health endpoint mapping |
| `appsettings.json` | New file | Full skeleton with all future sections as placeholders |
| `appsettings.Development.json` | New file | EnableConsole: true, localhost OTLP endpoint |
| `deploy/` folder | New files | docker-compose.yml, otel-collector-config.yaml (prometheusremotewrite), prometheus.yml, grafana provisioning |

---

## Sources

### Primary (HIGH confidence)

- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` — Reference implementation for all DI extension methods
- `src/Simetra/Telemetry/SimetraConsoleFormatter.cs` — Reference implementation for console formatter and lazy DI injection pattern
- `src/Simetra/Pipeline/RotatingCorrelationService.cs` — Reference implementation for lock-free correlation service
- `src/Simetra/Jobs/CorrelationJob.cs` — Reference implementation for Quartz correlation job
- `src/Simetra/Telemetry/SimetraLogEnrichmentProcessor.cs` — Reference implementation for OTLP log enrichment
- `src/Simetra/Configuration/` — Reference for all options classes and validators
- `src/Simetra/Simetra.csproj` — OTel package versions (all 1.15.0) confirmed
- `deploy/k8s/production/otel-collector.yaml` — OTel Collector container image version (0.120.0)
- `.planning/research/STACK.md` — Package versions verified against NuGet (2026-03-04)
- `.planning/research/PITFALLS.md` — `AsyncLocal` pitfalls, OTel flush on shutdown

### Secondary (MEDIUM confidence)

- `.planning/codebase/STACK.md` — Technology inventory, confirmed Quartz 3.15.1 in use by Simetra
- `.planning/codebase/ARCHITECTURE.md` — DI registration order, startup sequence
- `.planning/codebase/CONVENTIONS.cs` — Sealed classes, naming, file-scoped namespaces

### Tertiary (LOW confidence)

- General knowledge: `--web.enable-remote-write-receiver` Prometheus flag behavior — not verified against official docs, but is a known requirement. Validate against Prometheus docs when writing docker-compose.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages verified in existing Simetra.csproj and .planning/research/STACK.md
- Architecture patterns: HIGH — direct code inspection of Simetra reference implementation
- Docker Compose / OTel Collector config: MEDIUM — Simetra uses `prometheus` exporter (scrape); this project needs `prometheusremotewriteexporter` which is confirmed contrib-only but exact YAML config syntax should be verified against OTel Collector contrib docs
- Prometheus remote_write flag: LOW — flag name known from general knowledge, behavior not verified against official docs for v3.2.1

**Research date:** 2026-03-05
**Valid until:** 2026-04-05 (OTel and Quartz package versions; container image versions may drift faster)
