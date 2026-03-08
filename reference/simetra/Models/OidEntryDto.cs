using Simetra.Configuration;

namespace Simetra.Models;

/// <summary>
/// Immutable runtime representation of a single OID entry within a poll definition.
/// Created from <see cref="OidEntryOptions"/> during startup; used throughout the pipeline.
/// </summary>
/// <param name="Oid">SNMP OID string (e.g., "1.3.6.1.4.1.9999.1.3.1.0").</param>
/// <param name="PropertyName">Property name for the polled value (e.g., "cpu_utilization").</param>
/// <param name="Role">Role of this OID in the metric (Metric value or Label).</param>
/// <param name="EnumMap">Optional mapping of integer SNMP values to human-readable string labels, or null if not applicable.</param>
public sealed record OidEntryDto(
    string Oid,
    string PropertyName,
    OidRole Role,
    IReadOnlyDictionary<int, string>? EnumMap);
