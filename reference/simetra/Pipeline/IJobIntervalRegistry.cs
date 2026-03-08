namespace Simetra.Pipeline;

/// <summary>
/// Registry mapping job keys to their configured trigger interval in seconds.
/// Populated during <c>AddScheduling</c> for each Quartz job, consumed by
/// <see cref="Simetra.HealthChecks.LivenessHealthCheck"/> to compute per-job
/// staleness thresholds.
/// </summary>
public interface IJobIntervalRegistry
{
    /// <summary>
    /// Registers (or overwrites) the interval for a job key.
    /// Called during Quartz job/trigger configuration in <c>AddScheduling</c>.
    /// </summary>
    /// <param name="jobKey">The unique job key (e.g., "heartbeat", "correlation").</param>
    /// <param name="intervalSeconds">The trigger repeat interval in seconds.</param>
    void Register(string jobKey, int intervalSeconds);

    /// <summary>
    /// Attempts to retrieve the registered interval for a job key.
    /// </summary>
    /// <param name="jobKey">The unique job key to query.</param>
    /// <param name="intervalSeconds">The interval in seconds, if found.</param>
    /// <returns><c>true</c> if the job key was found; otherwise <c>false</c>.</returns>
    bool TryGetInterval(string jobKey, out int intervalSeconds);
}
