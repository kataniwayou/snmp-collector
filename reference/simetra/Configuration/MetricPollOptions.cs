using System.Text.Json.Serialization;

namespace Simetra.Configuration;

/// <summary>
/// Configuration for a single metric polling operation on a device.
/// </summary>
public sealed class MetricPollOptions
{
    /// <summary>
    /// Name of the metric to emit (e.g., "simetra_cpu").
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// Type of metric (Gauge or Counter).
    /// </summary>
    public MetricType MetricType { get; set; }

    /// <summary>
    /// OID entries to poll for this metric.
    /// </summary>
    public List<OidEntryOptions> Oids { get; set; } = [];

    /// <summary>
    /// Polling interval in seconds.
    /// </summary>
    public int IntervalSeconds { get; set; }

    /// <summary>
    /// Source of this metric poll definition. Set programmatically via PostConfigure --
    /// NOT bound from JSON configuration. Configuration-loaded polls are set to
    /// MetricPollSource.Configuration; module-discovered polls use MetricPollSource.Module.
    /// </summary>
    [JsonIgnore]
    public MetricPollSource Source { get; set; }

    /// <summary>
    /// Optional static labels injected into every metric produced by this poll definition.
    /// Keys must be snake_case; values must be non-empty strings. Used for identity encoded
    /// in OID path (e.g., OBP link number) rather than varbind data. Binds naturally from
    /// JSON: <c>"StaticLabels": { "link_number": "1" }</c>.
    /// </summary>
    public Dictionary<string, string>? StaticLabels { get; set; }
}
