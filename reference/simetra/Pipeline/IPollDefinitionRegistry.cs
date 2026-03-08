using System.Diagnostics.CodeAnalysis;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Provides O(1) lookup for poll definitions by (deviceName, pollKey) composite key.
/// Poll jobs receive device name and poll key from their Quartz JobDataMap and use
/// this registry to resolve the full <see cref="PollDefinitionDto"/> at execution time.
/// The pollKey is <see cref="PollDefinitionDto.PollKey"/> which combines MetricName with
/// StaticLabels for uniqueness (e.g., "fan_status-1" for fan 1).
/// </summary>
public interface IPollDefinitionRegistry
{
    /// <summary>
    /// Attempts to find a poll definition by device name and poll key.
    /// Lookup is case-insensitive on both keys.
    /// </summary>
    /// <param name="deviceName">The device name from JobDataMap.</param>
    /// <param name="pollKey">The poll key from JobDataMap (<see cref="PollDefinitionDto.PollKey"/>).</param>
    /// <param name="definition">The poll definition if found; null otherwise.</param>
    /// <returns>True if the definition was found; false otherwise.</returns>
    bool TryGetDefinition(string deviceName, string pollKey, [NotNullWhen(true)] out PollDefinitionDto? definition);

    /// <summary>
    /// Returns all state poll definitions (Source=Module) as (deviceName, definition) tuples.
    /// Used for dynamic Quartz job registration at startup.
    /// </summary>
    IReadOnlyList<(string DeviceName, PollDefinitionDto Definition)> GetAllStatePollDefinitions();

    /// <summary>
    /// Returns all metric poll definitions (Source=Configuration) as (deviceName, definition) tuples.
    /// Used for dynamic Quartz job registration at startup.
    /// </summary>
    IReadOnlyList<(string DeviceName, PollDefinitionDto Definition)> GetAllMetricPollDefinitions();
}
