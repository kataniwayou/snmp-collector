using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// A single State Vector entry holding the last-known domain data for a device/metric combination.
/// Contains the full <see cref="ExtractionResult"/>, the timestamp of the last update, and the
/// correlation ID for tracing the originating poll or trap.
/// </summary>
public sealed class StateVectorEntry
{
    /// <summary>
    /// The extraction result containing metrics, labels, and enum-map metadata.
    /// </summary>
    public required ExtractionResult Result { get; init; }

    /// <summary>
    /// UTC timestamp of when this entry was last updated.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Correlation ID linking this entry to the originating poll or trap cycle.
    /// </summary>
    public required string CorrelationId { get; init; }
}
