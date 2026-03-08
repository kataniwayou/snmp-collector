using System.ComponentModel.DataAnnotations;

namespace Simetra.Configuration;

/// <summary>
/// Channel capacity configuration. Bound from "Channels" section.
/// </summary>
public sealed class ChannelsOptions
{
    public const string SectionName = "Channels";

    /// <summary>
    /// Maximum number of items in bounded channels before backpressure is applied.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int BoundedCapacity { get; set; } = 100;
}
