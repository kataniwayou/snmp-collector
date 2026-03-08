using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Simetra.Extensions;
using Simetra.Pipeline;

var builder = WebApplication.CreateBuilder(args);

// DI registration order (see 11-Step Startup Sequence in ServiceCollectionExtensions.cs):
// 1. Telemetry (registered first = disposed last)
// 2. Configuration (ValidateOnStart for fail-fast)
// 3. DeviceModules (IDeviceModule singletons)
// 4. SnmpPipeline (registry, channels, listener)
// 5. ProcessingPipeline (metrics, state vector, coordinator)
// 6. Scheduling (Quartz, jobs, triggers)
// 7. HealthChecks (startup, readiness, liveness)
// 8. Lifecycle (GracefulShutdownService -- MUST BE LAST, stops FIRST)
builder.AddSimetraTelemetry();
builder.Services.AddSimetraConfiguration(builder.Configuration);
builder.Services.AddDeviceModules();
builder.Services.AddSnmpPipeline();
builder.Services.AddProcessingPipeline();
builder.Services.AddScheduling(builder.Configuration);
builder.Services.AddSimetraHealthChecks();
builder.Services.AddSimetraLifecycle();

var app = builder.Build();

// LIFE-02: Generate first correlationId directly on startup before any job fires
var correlationService = app.Services.GetRequiredService<ICorrelationService>();
correlationService.SetCorrelationId(Guid.NewGuid().ToString("N"));

// TELEM-05 + LIFE-07: Telemetry ForceFlush is handled by GracefulShutdownService
// (time-budgeted, runs as final protected step during graceful shutdown).

// Health probe endpoints with tag-filtered checks and explicit status codes.
// Each endpoint runs only the health check(s) matching its tag.
app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

try
{
    app.Run();
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine("Configuration validation failed:");
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }

    throw;
}
