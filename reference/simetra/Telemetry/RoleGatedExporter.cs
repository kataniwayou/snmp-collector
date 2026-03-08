using OpenTelemetry;

namespace Simetra.Telemetry;

/// <summary>
/// Decorator that wraps a <see cref="BaseExporter{T}"/> and gates export on leader status.
/// When this instance is a follower, batches are silently dropped (returning Success,
/// not Failure, to avoid triggering retry logic in the SDK).
/// Designed in this plan but NOT wired into the OTLP exporter chain -- Phase 8 handles wiring.
/// </summary>
public sealed class RoleGatedExporter<T> : BaseExporter<T> where T : class
{
    private readonly BaseExporter<T> _inner;
    private readonly ILeaderElection _leaderElection;

    public RoleGatedExporter(BaseExporter<T> inner, ILeaderElection leaderElection)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
    }

    public override ExportResult Export(in Batch<T> batch)
    {
        if (!_leaderElection.IsLeader)
        {
            // Silently drop -- returning Success prevents SDK retry backoff
            return ExportResult.Success;
        }

        return _inner.Export(batch);
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        return _inner.ForceFlush(timeoutMilliseconds);
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        return _inner.Shutdown(timeoutMilliseconds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
