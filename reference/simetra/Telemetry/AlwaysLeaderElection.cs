namespace Simetra.Telemetry;

/// <summary>
/// Default ILeaderElection implementation that always reports leader status.
/// Suitable for single-instance deployments and local development.
/// Replaced by distributed lease-based implementation in Phase 8 (HA).
/// </summary>
public sealed class AlwaysLeaderElection : ILeaderElection
{
    /// <inheritdoc />
    public bool IsLeader => true;

    /// <inheritdoc />
    public string CurrentRole => "leader";
}
