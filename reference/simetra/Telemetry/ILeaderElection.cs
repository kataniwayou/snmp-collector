namespace Simetra.Telemetry;

/// <summary>
/// Abstraction for leader election status. Phase 8 (HA) provides a real implementation
/// backed by distributed lease. Default implementation always reports leader.
/// </summary>
public interface ILeaderElection
{
    /// <summary>
    /// Returns whether this instance is the active leader.
    /// </summary>
    bool IsLeader { get; }

    /// <summary>
    /// Returns "leader" or "follower" for log enrichment and telemetry tagging.
    /// </summary>
    string CurrentRole { get; }
}
