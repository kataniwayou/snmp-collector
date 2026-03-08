using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;
using Simetra.Pipeline;

namespace Simetra.HealthChecks;

/// <summary>
/// Readiness health check (HLTH-04). Returns Healthy when device channels are
/// registered and the Quartz scheduler is running. Kubernetes readiness probe
/// controls load balancer routing -- traffic is only sent to ready pods.
/// </summary>
public sealed class ReadinessHealthCheck : IHealthCheck
{
    private readonly IDeviceChannelManager _channels;
    private readonly ISchedulerFactory _schedulerFactory;

    public ReadinessHealthCheck(
        IDeviceChannelManager channels,
        ISchedulerFactory schedulerFactory)
    {
        _channels = channels;
        _schedulerFactory = schedulerFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // HLTH-04: Check all device channels are open
        if (_channels.DeviceNames.Count == 0)
        {
            return HealthCheckResult.Unhealthy("No device channels registered");
        }

        // HLTH-04: Check Quartz scheduler is running
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        if (!scheduler.IsStarted || scheduler.IsShutdown)
        {
            return HealthCheckResult.Unhealthy("Quartz scheduler is not running");
        }

        return HealthCheckResult.Healthy();
    }
}
