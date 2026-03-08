namespace Simetra.Telemetry;

/// <summary>
/// Shared constants for OpenTelemetry meter and tracing source names.
/// LeaderMeterName MUST match the string used in MetricFactory: meterFactory.Create(TelemetryConstants.LeaderMeterName).
/// </summary>
public static class TelemetryConstants
{
    /// <summary>
    /// Leader-only device metrics meter. Gated by MetricRoleGatedExporter --
    /// only the leader exports these (NPB, OBP, heartbeat device metric).
    /// </summary>
    public const string LeaderMeterName = "Simetra.Leader";

    /// <summary>
    /// Pipeline business metrics meter -- exported by ALL pods (not gated).
    /// Used by PipelineMetricService for operational metrics (trap rate, poll count,
    /// channel health, heartbeat pipeline duration).
    /// </summary>
    public const string InstanceMeterName = "Simetra.Instance";

    /// <summary>
    /// ActivitySource name subscribed to by TracerProvider.
    /// </summary>
    public const string TracingSourceName = "Simetra.Tracing";
}
