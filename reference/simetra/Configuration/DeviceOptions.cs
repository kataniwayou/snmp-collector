namespace Simetra.Configuration;

/// <summary>
/// Configuration for a single monitored device.
/// Nested inside DevicesOptions -- not a standalone IOptions registration.
/// </summary>
public sealed class DeviceOptions
{
    /// <summary>
    /// Human-readable device name (e.g., "router-core-1").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the device for SNMP polling.
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Device type identifier (e.g., "router", "switch", "loadbalancer", "simetra").
    /// Must be a registered device type.
    /// </summary>
    public string DeviceType { get; set; } = string.Empty;

    /// <summary>
    /// Metric polling configurations for this device.
    /// </summary>
    public List<MetricPollOptions> MetricPolls { get; set; } = [];
}
