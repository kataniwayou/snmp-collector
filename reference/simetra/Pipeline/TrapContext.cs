namespace Simetra.Pipeline;

/// <summary>
/// Processing context for a single trap, used by middleware to enrich, filter,
/// or reject the trap before it is routed to a device channel.
/// </summary>
public sealed class TrapContext
{
    /// <summary>
    /// The trap data being processed.
    /// </summary>
    public required TrapEnvelope Envelope { get; init; }

    /// <summary>
    /// Device information resolved from the sender IP, or null if the device is unknown.
    /// Set by the device filter middleware.
    /// </summary>
    public DeviceInfo? Device { get; set; }

    /// <summary>
    /// Whether any filter or middleware has rejected this trap.
    /// </summary>
    public bool IsRejected { get; set; }

    /// <summary>
    /// Human-readable reason for rejection, or null if not rejected.
    /// </summary>
    public string? RejectionReason { get; set; }
}
