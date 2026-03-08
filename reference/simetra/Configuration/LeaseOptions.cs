using System.ComponentModel.DataAnnotations;

namespace Simetra.Configuration;

/// <summary>
/// Leader election lease configuration. Bound from "Lease" section.
/// </summary>
public sealed class LeaseOptions
{
    public const string SectionName = "Lease";

    /// <summary>
    /// Lease resource name in the coordination system.
    /// </summary>
    [Required]
    public required string Name { get; set; } = "simetra-leader";

    /// <summary>
    /// Namespace for the lease resource.
    /// </summary>
    [Required]
    public required string Namespace { get; set; } = "simetra";

    /// <summary>
    /// How often the leader renews its lease, in seconds.
    /// </summary>
    [Range(1, 300)]
    public int RenewIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Total lease duration in seconds. Must be greater than RenewIntervalSeconds.
    /// </summary>
    [Range(1, 600)]
    public int DurationSeconds { get; set; } = 15;
}
