using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// In-memory State Vector for tracking last-known domain data per device/metric combination.
/// Entries are stored with no persistence and no TTL -- last-write-wins semantics.
/// The State Vector is the data source for Module-sourced definitions that derive additional
/// metrics from previously collected state.
/// </summary>
public interface IStateVectorService
{
    /// <summary>
    /// Updates (or creates) the state entry for the specified device and metric name.
    /// Uses last-write-wins semantics via atomic AddOrUpdate.
    /// </summary>
    /// <param name="deviceName">Device name (e.g., "router-core-1").</param>
    /// <param name="metricName">Metric definition name (e.g., "simetra_cpu").</param>
    /// <param name="result">The extraction result to store.</param>
    /// <param name="correlationId">Correlation ID of the originating poll or trap.</param>
    void Update(string deviceName, string metricName, ExtractionResult result, string correlationId);

    /// <summary>
    /// Returns the state entry for the specified device and metric name, or null if not present.
    /// </summary>
    /// <param name="deviceName">Device name.</param>
    /// <param name="metricName">Metric definition name.</param>
    /// <returns>The state entry, or null if no entry exists for this combination.</returns>
    StateVectorEntry? GetEntry(string deviceName, string metricName);

    /// <summary>
    /// Returns a snapshot of all state entries. The returned dictionary is a point-in-time copy,
    /// not a live view -- suitable for health probes and diagnostics.
    /// </summary>
    /// <returns>Read-only dictionary of all state entries keyed by "deviceName:metricName".</returns>
    IReadOnlyDictionary<string, StateVectorEntry> GetAllEntries();
}
