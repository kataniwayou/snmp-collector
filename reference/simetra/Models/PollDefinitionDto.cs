using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Simetra.Configuration;

namespace Simetra.Models;

/// <summary>
/// Immutable runtime representation of a metric poll definition.
/// Created from <see cref="MetricPollOptions"/> via <see cref="FromOptions"/>; used by the
/// extraction pipeline for both trap and poll processing.
/// </summary>
/// <param name="MetricName">Name of the metric to emit (e.g., "simetra_cpu").</param>
/// <param name="MetricType">Type of metric (Gauge or Counter).</param>
/// <param name="Oids">Ordered list of OID entries to poll for this metric.</param>
/// <param name="IntervalSeconds">Polling interval in seconds.</param>
/// <param name="Source">Origin of this poll definition (Configuration or Module).</param>
/// <param name="StaticLabels">Optional static labels injected into every metric produced by this
/// poll definition. Keys must be snake_case; values must be non-empty strings. Used for identity
/// encoded in OID path (e.g., OBP link number) rather than varbind data.</param>
public sealed record PollDefinitionDto
{
    private static readonly Regex SnakeCasePattern = new(
        @"^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly HashSet<string> ReservedBaseLabels = new(StringComparer.Ordinal)
    {
        "site_name", "device_name", "device_ip", "device_type"
    };

    public string MetricName { get; init; }
    public MetricType MetricType { get; init; }
    public IReadOnlyList<OidEntryDto> Oids { get; init; }
    public int IntervalSeconds { get; init; }
    public MetricPollSource Source { get; init; }
    public IReadOnlyDictionary<string, string>? StaticLabels { get; init; }

    /// <summary>
    /// Unique key for this poll definition within a device. Combines <see cref="MetricName"/>
    /// with sorted <see cref="StaticLabels"/> values to disambiguate polls that share a
    /// MetricName (e.g., fan_status for fans 1-4). Used as the discriminator in
    /// <see cref="Simetra.Pipeline.PollDefinitionRegistry"/> and Quartz job keys.
    /// </summary>
    public string PollKey => StaticLabels is { Count: > 0 }
        ? $"{MetricName}-{string.Join("-", StaticLabels.OrderBy(kv => kv.Key).Select(kv => kv.Value))}"
        : MetricName;

    public PollDefinitionDto(
        string MetricName,
        MetricType MetricType,
        IReadOnlyList<OidEntryDto> Oids,
        int IntervalSeconds,
        MetricPollSource Source,
        IReadOnlyDictionary<string, string>? StaticLabels = null)
    {
        this.MetricName = MetricName;
        this.MetricType = MetricType;
        this.Oids = Oids;
        this.IntervalSeconds = IntervalSeconds;
        this.Source = Source;
        this.StaticLabels = StaticLabels;

        ValidateStaticLabels(StaticLabels);
    }

    private static void ValidateStaticLabels(IReadOnlyDictionary<string, string>? staticLabels)
    {
        if (staticLabels is not { Count: > 0 })
            return;

        foreach (var (key, value) in staticLabels)
        {
            if (!SnakeCasePattern.IsMatch(key))
                throw new ArgumentException(
                    $"StaticLabel key '{key}' must be snake_case (matching ^[a-z][a-z0-9]*(_[a-z0-9]+)*$).");

            if (ReservedBaseLabels.Contains(key))
                throw new ArgumentException(
                    $"StaticLabel key '{key}' conflicts with reserved base label name.");

            if (string.IsNullOrEmpty(value))
                throw new ArgumentException(
                    $"StaticLabel value for key '{key}' must be non-null and non-empty.");
        }
    }

    /// <summary>
    /// Converts a mutable <see cref="MetricPollOptions"/> configuration object into an
    /// immutable <see cref="PollDefinitionDto"/> for runtime use. The Source field is
    /// preserved from <paramref name="options"/> (already stamped by PostConfigure).
    /// </summary>
    /// <param name="options">The mutable configuration options to convert.</param>
    /// <returns>An immutable poll definition DTO.</returns>
    public static PollDefinitionDto FromOptions(MetricPollOptions options)
    {
        var oids = options.Oids
            .Select(o => new OidEntryDto(
                o.Oid,
                o.PropertyName,
                o.Role,
                o.EnumMap?.ToDictionary(kv => kv.Key, kv => kv.Value).AsReadOnly()))
            .ToList()
            .AsReadOnly();

        var staticLabels = options.StaticLabels?
            .ToDictionary(kv => kv.Key, kv => kv.Value)
            .AsReadOnly();

        return new PollDefinitionDto(
            options.MetricName,
            options.MetricType,
            oids,
            options.IntervalSeconds,
            options.Source,
            staticLabels);
    }
}
