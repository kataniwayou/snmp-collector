using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Immutable runtime representation of a monitored device, holding its identity
/// and the trap definitions converted from configuration at startup.
/// </summary>
/// <param name="Name">Human-readable device name (e.g., "router-core-1").</param>
/// <param name="IpAddress">IPv4 address string of the device.</param>
/// <param name="DeviceType">Device type identifier (e.g., "router", "switch").</param>
/// <param name="TrapDefinitions">Poll definitions applicable to traps from this device.</param>
public sealed record DeviceInfo(
    string Name,
    string IpAddress,
    string DeviceType,
    IReadOnlyList<PollDefinitionDto> TrapDefinitions);
