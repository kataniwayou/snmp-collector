using Microsoft.Extensions.Diagnostics.HealthChecks;
using Simetra.Pipeline;

namespace Simetra.HealthChecks;

/// <summary>
/// Startup health check (HLTH-03). Returns Healthy when the pipeline is wired
/// (services resolved) and the first correlationId has been generated.
/// Kubernetes startup probe gates readiness and liveness probes until this succeeds.
/// </summary>
public sealed class StartupHealthCheck : IHealthCheck
{
    private readonly ICorrelationService _correlation;

    public StartupHealthCheck(ICorrelationService correlation)
    {
        _correlation = correlation;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var hasCorrelation = !string.IsNullOrEmpty(_correlation.CurrentCorrelationId);

        return Task.FromResult(hasCorrelation
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("First correlationId not yet generated"));
    }
}
