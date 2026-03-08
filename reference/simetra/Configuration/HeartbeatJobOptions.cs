using System.ComponentModel.DataAnnotations;

namespace Simetra.Configuration;

/// <summary>
/// Heartbeat job timing configuration. Bound from "HeartbeatJob" section.
/// </summary>
public sealed class HeartbeatJobOptions
{
    public const string SectionName = "HeartbeatJob";

    /// <summary>
    /// Interval between heartbeat emissions, in seconds.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int IntervalSeconds { get; set; } = 15;
}
