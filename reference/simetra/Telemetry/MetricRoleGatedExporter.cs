using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Simetra.Telemetry;

/// <summary>
/// Metric-specific role-gated exporter that gates only metrics from a specific meter
/// (e.g., "Simetra.Leader") behind leader election, while allowing all other meters
/// (e.g., System.Runtime, Simetra.Instance, Simetra.Role) to export on every instance
/// regardless of role. This ensures runtime, pipeline, and role metrics are always
/// exported by both leader and follower pods for operational visibility.
/// </summary>
public sealed class MetricRoleGatedExporter : BaseExporter<Metric>
{
    private static readonly PropertyInfo ParentProviderProperty =
        typeof(BaseExporter<Metric>).GetProperty("ParentProvider")!;

    private readonly BaseExporter<Metric> _inner;
    private readonly ILeaderElection _leaderElection;
    private readonly string _gatedMeterName;
    private bool _parentProviderPropagated;

    public MetricRoleGatedExporter(
        BaseExporter<Metric> inner,
        ILeaderElection leaderElection,
        string gatedMeterName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
        _gatedMeterName = gatedMeterName ?? throw new ArgumentNullException(nameof(gatedMeterName));
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        // Propagate ParentProvider to inner exporter so OTLP export includes
        // resource attributes (service.name, service.instance.id).
        // ParentProvider has an internal setter, so reflection is required.
        if (!_parentProviderPropagated && ParentProvider != null)
        {
            ParentProviderProperty.SetValue(_inner, ParentProvider);
            _parentProviderPropagated = true;
        }

        if (_leaderElection.IsLeader)
        {
            // Leader: export everything (Simetra.Leader + Instance + Role + runtime)
            return _inner.Export(batch);
        }

        // Follower: filter out gated meter, export only runtime metrics
        var ungated = new List<Metric>();
        foreach (var metric in batch)
        {
            if (!string.Equals(metric.MeterName, _gatedMeterName, StringComparison.Ordinal))
            {
                ungated.Add(metric);
            }
        }

        if (ungated.Count == 0)
            return ExportResult.Success;

        var filteredBatch = new Batch<Metric>(ungated.ToArray(), ungated.Count);
        return _inner.Export(filteredBatch);
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
