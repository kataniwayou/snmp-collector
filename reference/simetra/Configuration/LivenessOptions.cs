using System.ComponentModel.DataAnnotations;

namespace Simetra.Configuration;

/// <summary>
/// Liveness detection configuration. Bound from "Liveness" section.
/// </summary>
public sealed class LivenessOptions
{
    public const string SectionName = "Liveness";

    /// <summary>
    /// Multiplier applied to heartbeat interval to determine device staleness.
    /// A device is considered stale after (HeartbeatInterval * GraceMultiplier) seconds.
    /// </summary>
    [Range(1.0, 100.0)]
    public double GraceMultiplier { get; set; } = 2.0;
}
