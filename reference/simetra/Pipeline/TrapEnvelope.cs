using System.Net;
using Lextm.SharpSnmpLib;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Immutable data envelope carrying a single SNMP trap through the pipeline.
/// Created by the SNMP listener, routed through device channels to Layer 3 processing.
/// </summary>
public sealed class TrapEnvelope
{
    /// <summary>
    /// SNMP varbinds received in the trap message.
    /// </summary>
    public required IList<Variable> Varbinds { get; init; }

    /// <summary>
    /// Sender IP address, normalized to IPv4 via <see cref="IPAddress.MapToIPv4"/>.
    /// </summary>
    public required IPAddress SenderAddress { get; init; }

    /// <summary>
    /// Timestamp when the trap was received by the listener.
    /// </summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>
    /// Correlation ID linking this trap to a correlation job. Initially empty;
    /// set by the listener or middleware after construction.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// The poll definition that matched this trap's OIDs, or null if unmatched.
    /// Set by <see cref="ITrapFilter"/> after OID matching so Layer 3 does not re-match.
    /// </summary>
    public PollDefinitionDto? MatchedDefinition { get; set; }
}
