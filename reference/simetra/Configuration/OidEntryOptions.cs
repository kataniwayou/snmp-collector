namespace Simetra.Configuration;

/// <summary>
/// Configuration for a single OID entry within a metric poll.
/// </summary>
public sealed class OidEntryOptions
{
    /// <summary>
    /// SNMP OID string (e.g., "1.3.6.1.4.1.9999.1.3.1.0").
    /// </summary>
    public string Oid { get; set; } = string.Empty;

    /// <summary>
    /// Property name for the polled value (e.g., "cpu_utilization").
    /// </summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// Role of this OID in the metric (Metric value or Label).
    /// </summary>
    public OidRole Role { get; set; }

    /// <summary>
    /// Optional mapping of integer SNMP values to human-readable string labels.
    /// Used for enum-style OIDs where integer values represent named states.
    /// </summary>
    public Dictionary<int, string>? EnumMap { get; set; }
}
