using Simetra.Models;

namespace Simetra.Devices;

/// <summary>
/// Type-level data contract for a device module that provides trap and poll definitions
/// for all devices of a given <see cref="DeviceType"/>. Implementations are registered
/// in DI and discovered automatically by <c>DeviceRegistry</c>, <c>PollDefinitionRegistry</c>,
/// and scheduling at startup. Device identity (Name, IP) comes from configuration —
/// the module provides only type-level OID definitions.
/// All poll definitions returned by a module must have <c>Source = MetricPollSource.Module</c>.
/// </summary>
public interface IDeviceModule
{
    /// <summary>
    /// Device type identifier (e.g., "simetra", "NPB", "OBP"). Matched against
    /// <see cref="Configuration.DeviceOptions.DeviceType"/> to apply module definitions
    /// to all config devices of this type.
    /// </summary>
    string DeviceType { get; }

    /// <summary>
    /// Trap definitions for this device type, used for OID matching on incoming traps.
    /// Applied to every config device whose DeviceType matches this module.
    /// All entries must have <c>Source = MetricPollSource.Module</c>.
    /// </summary>
    IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; }

    /// <summary>
    /// State poll definitions for this device type, used for periodic SNMP polling.
    /// Applied to every config device whose DeviceType matches this module.
    /// All entries must have <c>Source = MetricPollSource.Module</c>.
    /// </summary>
    IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; }
}

/// <summary>
/// Marker sub-interface for device modules that represent virtual (internal) devices
/// with a fixed name and IP address. Virtual devices are auto-registered by
/// <c>DeviceRegistry</c> and <c>DeviceChannelManager</c> without requiring config entries.
/// </summary>
public interface IVirtualDeviceModule : IDeviceModule
{
    /// <summary>
    /// The fixed device name for this virtual device (e.g., "simetra-supervisor").
    /// </summary>
    string VirtualDeviceName { get; }

    /// <summary>
    /// The fixed IP address for this virtual device (e.g., "127.0.0.1").
    /// </summary>
    string VirtualDeviceIpAddress { get; }
}
