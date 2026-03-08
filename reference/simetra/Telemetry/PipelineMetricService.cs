using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Simetra.Configuration;

namespace Simetra.Telemetry;

/// <summary>
/// Singleton service owning the <c>Simetra.Instance</c> meter with 5 pipeline instruments
/// plus the <c>simetra.role.is_leader</c> observable gauge (consolidated from the former
/// <c>Simetra.Role</c> meter / <c>RoleMetricService</c>).
/// All metrics are exported by ALL pods (not gated by <see cref="MetricRoleGatedExporter"/>),
/// enabling failover readiness comparison across leader and follower instances.
/// </summary>
public sealed class PipelineMetricService : IDisposable
{
    private readonly Meter _meter;
    private readonly string _siteName;
    private readonly Histogram<double> _trapReceived;
    private readonly Histogram<double> _pollExecuted;
    private readonly Histogram<double> _heartbeatProcessed;
    private readonly Histogram<double> _heartbeatDuration;

    /// <summary>
    /// Initializes a new instance of <see cref="PipelineMetricService"/>.
    /// Creates the <c>Simetra.Instance</c> meter, pre-creates all 5 pipeline instruments,
    /// and registers the <c>simetra.role.is_leader</c> observable gauge (replacing the
    /// former <c>RoleMetricService</c> / <c>Simetra.Role</c> meter).
    /// </summary>
    /// <param name="meterFactory">DI-provided meter factory for creating named meters.</param>
    /// <param name="siteOptions">Site configuration providing the site name for base labels.</param>
    /// <param name="leaderElection">Leader election abstraction for the is_leader observable gauge callback.</param>
    public PipelineMetricService(IMeterFactory meterFactory, IOptions<SiteOptions> siteOptions, ILeaderElection leaderElection)
    {
        _siteName = siteOptions.Value.Name;
        _meter = meterFactory.Create(TelemetryConstants.InstanceMeterName);

        _trapReceived = _meter.CreateHistogram<double>(
            "simetra.trap.received",
            description: "SNMP traps received and routed to device channels");

        _pollExecuted = _meter.CreateHistogram<double>(
            "simetra.poll.executed",
            description: "SNMP polls executed (state + metric)");

        _heartbeatProcessed = _meter.CreateHistogram<double>(
            "simetra.heartbeat.processed",
            description: "Heartbeat traps processed through the pipeline");

        _heartbeatDuration = _meter.CreateHistogram<double>(
            "simetra.heartbeat.duration",
            unit: "s",
            description: "Time for heartbeat trap to traverse the full pipeline",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0]
            });

        // Observable gauge for leader/follower role (consolidated from former RoleMetricService).
        // Uses Measurement<int> overload to include site_name tag on the gauge.
        _meter.CreateObservableGauge(
            "simetra.role.is_leader",
            () => new Measurement<int>(
                leaderElection.IsLeader ? 1 : 0,
                new TagList { { "site_name", _siteName } }),
            description: "1 if this pod is the leader, 0 if follower");
    }

    /// <summary>
    /// Records that an SNMP trap was received and routed to a device channel.
    /// </summary>
    /// <param name="tags">Base labels including site_name, device_name, device_ip, device_type.</param>
    public void RecordTrapReceived(TagList tags) => _trapReceived.Record(1, tags);

    /// <summary>
    /// Records that an SNMP poll (state or metric) was executed successfully.
    /// </summary>
    /// <param name="tags">Base labels including site_name, device_name, device_ip, device_type.</param>
    public void RecordPollExecuted(TagList tags) => _pollExecuted.Record(1, tags);

    /// <summary>
    /// Records that a heartbeat trap was processed through the pipeline.
    /// Per-pod metric -- no device labels needed.
    /// </summary>
    public void RecordHeartbeatProcessed() => _heartbeatProcessed.Record(1);

    /// <summary>
    /// Records the duration of a heartbeat trap traversing the full pipeline.
    /// Per-pod metric -- no device labels needed.
    /// </summary>
    /// <param name="seconds">Duration in seconds from listener receive to processing complete.</param>
    public void RecordHeartbeatDuration(double seconds) => _heartbeatDuration.Record(seconds);

    /// <summary>
    /// Builds a <see cref="TagList"/> with the standard base labels for per-device metrics.
    /// Matches the label pattern used by <see cref="Pipeline.MetricFactory"/>.
    /// </summary>
    /// <param name="deviceName">Device name (e.g., "npb-edge-01").</param>
    /// <param name="deviceIp">Device IP address (e.g., "10.0.0.1").</param>
    /// <param name="deviceType">Device type identifier (e.g., "npb").</param>
    /// <returns>A <see cref="TagList"/> containing site_name, device_name, device_ip, device_type tags.</returns>
    public TagList BuildBaseLabels(string deviceName, string deviceIp, string deviceType)
    {
        return new TagList
        {
            { "site_name", _siteName },
            { "device_name", deviceName },
            { "device_ip", deviceIp },
            { "device_type", deviceType }
        };
    }

    /// <summary>
    /// Disposes the underlying meter, releasing all instruments.
    /// </summary>
    public void Dispose() => _meter.Dispose();
}
