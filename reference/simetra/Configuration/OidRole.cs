using System.Text.Json.Serialization;

namespace Simetra.Configuration;

/// <summary>
/// Role of an OID entry within a metric poll.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OidRole
{
    /// <summary>
    /// This OID provides the metric value.
    /// </summary>
    Metric,

    /// <summary>
    /// This OID provides a label/dimension for the metric.
    /// </summary>
    Label
}
