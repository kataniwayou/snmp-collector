using System.Text.Json.Serialization;

namespace Simetra.Configuration;

/// <summary>
/// Type of SNMP metric measurement.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MetricType
{
    /// <summary>
    /// A point-in-time measurement that can go up or down (e.g., CPU utilization).
    /// </summary>
    Gauge,

    /// <summary>
    /// A monotonically increasing value (e.g., total bytes transferred).
    /// </summary>
    Counter
}
