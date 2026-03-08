namespace Simetra.Configuration;

/// <summary>
/// Indicates the origin of a metric poll definition.
/// This enum is not serialized to/from JSON -- it is set programmatically.
/// </summary>
public enum MetricPollSource
{
    /// <summary>
    /// Poll was defined in appsettings.json configuration.
    /// </summary>
    Configuration,

    /// <summary>
    /// Poll was discovered dynamically by a device module.
    /// </summary>
    Module
}
