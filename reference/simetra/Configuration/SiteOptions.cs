using System.ComponentModel.DataAnnotations;

namespace Simetra.Configuration;

/// <summary>
/// Site identification configuration. Bound from "Site" section.
/// </summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// Unique site name identifier (e.g., "site-nyc-01"). Required.
    /// </summary>
    [Required]
    public required string Name { get; set; }

    /// <summary>
    /// Pod identity for leader election. Defaults to HOSTNAME env var via PostConfigure
    /// when not explicitly set in configuration.
    /// </summary>
    public string? PodIdentity { get; set; }
}
