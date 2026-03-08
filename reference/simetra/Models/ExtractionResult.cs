using System.Collections.ObjectModel;

namespace Simetra.Models;

/// <summary>
/// Container for the output of an SNMP extraction operation. Holds the raw numeric metrics,
/// string labels, and enum-map metadata produced by processing varbinds against a
/// <see cref="PollDefinitionDto"/>.
/// </summary>
public sealed class ExtractionResult
{
    /// <summary>
    /// The poll definition that produced this result.
    /// </summary>
    public PollDefinitionDto Definition { get; init; } = null!;

    /// <summary>
    /// Raw SNMP numeric values keyed by PropertyName. EnumMap is NOT applied to these values --
    /// they remain as raw numeric values cast to double for metric emission.
    /// </summary>
    public IReadOnlyDictionary<string, double> Metrics { get; init; } =
        ReadOnlyDictionary<string, double>.Empty;

    /// <summary>
    /// Enum-mapped strings or raw string values keyed by PropertyName. For OIDs with an EnumMap,
    /// the integer value is resolved to the mapped string. For OIDs without an EnumMap, the raw
    /// SNMP string value is used directly.
    /// </summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Enum-map metadata keyed by PropertyName, stored for Grafana value mappings.
    /// These are NOT used as metric values -- they are passed through to the telemetry emitter
    /// so Grafana dashboards can resolve integer metric values to display names.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> EnumMapMetadata { get; init; } =
        ReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>.Empty;
}
