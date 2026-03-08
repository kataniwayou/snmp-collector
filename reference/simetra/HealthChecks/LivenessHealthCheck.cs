using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Pipeline;

namespace Simetra.HealthChecks;

/// <summary>
/// Liveness health check (HLTH-05/06/07). Iterates all liveness vector stamps and
/// compares each job's stamp age against its configured interval multiplied by the
/// grace multiplier. Returns Unhealthy with diagnostic log when any stamp is stale;
/// returns Healthy silently (no log) when all stamps are fresh.
/// </summary>
public sealed class LivenessHealthCheck : IHealthCheck
{
    private readonly ILivenessVectorService _liveness;
    private readonly IJobIntervalRegistry _intervals;
    private readonly double _graceMultiplier;
    private readonly ILogger<LivenessHealthCheck> _logger;

    public LivenessHealthCheck(
        ILivenessVectorService liveness,
        IJobIntervalRegistry intervals,
        IOptions<LivenessOptions> options,
        ILogger<LivenessHealthCheck> logger)
    {
        _liveness = liveness;
        _intervals = intervals;
        _graceMultiplier = options.Value.GraceMultiplier;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var stamps = _liveness.GetAllStamps();
        var now = DateTimeOffset.UtcNow;
        var staleEntries = new Dictionary<string, object>();

        foreach (var (jobKey, lastStamp) in stamps)
        {
            if (!_intervals.TryGetInterval(jobKey, out var intervalSeconds))
                continue; // unknown job, skip

            var threshold = TimeSpan.FromSeconds(intervalSeconds * _graceMultiplier);
            var age = now - lastStamp;

            if (age > threshold)
            {
                staleEntries[jobKey] = new
                {
                    ageSeconds = Math.Round(age.TotalSeconds, 1),
                    thresholdSeconds = threshold.TotalSeconds,
                    lastStamp = lastStamp.ToString("O")
                };
            }
        }

        if (staleEntries.Count > 0)
        {
            // HLTH-05: 503 with diagnostic log
            _logger.LogWarning(
                "Liveness check failed: {StaleCount} stale job(s) detected",
                staleEntries.Count);

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"{staleEntries.Count} stale job(s)",
                data: staleEntries.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value,
                    StringComparer.Ordinal) as IReadOnlyDictionary<string, object>));
        }

        // HLTH-07: Healthy returns 200 silently (no log)
        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
