namespace Simetra.Pipeline;

/// <summary>
/// Singleton registry of job trigger intervals, populated during <c>AddScheduling</c>.
/// Backed by a <see cref="Dictionary{TKey, TValue}"/> with ordinal string comparison.
/// </summary>
public sealed class JobIntervalRegistry : IJobIntervalRegistry
{
    private readonly Dictionary<string, int> _intervals = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Register(string jobKey, int intervalSeconds)
    {
        _intervals[jobKey] = intervalSeconds;
    }

    /// <inheritdoc />
    public bool TryGetInterval(string jobKey, out int intervalSeconds)
    {
        return _intervals.TryGetValue(jobKey, out intervalSeconds);
    }
}
