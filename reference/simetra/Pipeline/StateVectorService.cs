using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// In-memory State Vector backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Stores the last-known <see cref="ExtractionResult"/> per device/metric combination using
/// a composite key of "deviceName:metricName". No persistence, no TTL -- entries live until
/// overwritten or the process restarts.
/// </summary>
public sealed class StateVectorService : IStateVectorService
{
    private readonly ILogger<StateVectorService> _logger;
    private readonly ConcurrentDictionary<string, StateVectorEntry> _entries = new();

    public StateVectorService(ILogger<StateVectorService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Update(string deviceName, string metricName, ExtractionResult result, string correlationId)
    {
        var key = $"{deviceName}:{metricName}";

        _entries.AddOrUpdate(
            key,
            _ => CreateEntry(result, correlationId),
            (_, _) => CreateEntry(result, correlationId));

        _logger.LogDebug(
            "State Vector updated for {DeviceName}:{MetricName}",
            deviceName,
            metricName);
    }

    /// <inheritdoc />
    public StateVectorEntry? GetEntry(string deviceName, string metricName)
    {
        var key = $"{deviceName}:{metricName}";
        return _entries.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, StateVectorEntry> GetAllEntries()
    {
        return new ReadOnlyDictionary<string, StateVectorEntry>(
            _entries.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static StateVectorEntry CreateEntry(ExtractionResult result, string correlationId)
    {
        return new StateVectorEntry
        {
            Result = result,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = correlationId
        };
    }
}
