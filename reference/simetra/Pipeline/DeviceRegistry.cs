using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Devices;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Singleton registry that maps normalized IPv4 addresses and device names to
/// <see cref="DeviceInfo"/> for O(1) device lookup. Built once at startup from
/// <see cref="DevicesOptions"/> and any registered <see cref="IDeviceModule"/> implementations.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly Dictionary<IPAddress, DeviceInfo> _devices;
    private readonly Dictionary<string, DeviceInfo> _devicesByName;

    /// <summary>
    /// Initializes the registry by building IP-to-device and name-to-device dictionaries
    /// from configuration. Each config device is matched to a code-defined module by
    /// <see cref="IDeviceModule.DeviceType"/> to attach module-level trap definitions.
    /// Devices without a matching module get empty trap definitions (poll-only devices).
    /// Each device's IP is normalized to IPv4 via <see cref="IPAddress.MapToIPv4"/>.
    /// </summary>
    /// <param name="devicesOptions">The configured devices to register.</param>
    /// <param name="modules">Code-defined device modules providing type-level trap definitions.</param>
    public DeviceRegistry(
        IOptions<DevicesOptions> devicesOptions,
        IEnumerable<IDeviceModule> modules)
    {
        var devices = devicesOptions.Value.Devices;
        _devices = new Dictionary<IPAddress, DeviceInfo>(devices.Count);
        _devicesByName = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);

        // Index modules by DeviceType for O(1) lookup
        var modulesByType = modules.ToDictionary(m => m.DeviceType, StringComparer.OrdinalIgnoreCase);

        foreach (var d in devices)
        {
            var ip = IPAddress.Parse(d.IpAddress).MapToIPv4();

            // Attach module trap definitions by matching DeviceType
            var trapDefinitions = modulesByType.TryGetValue(d.DeviceType, out var module)
                ? module.TrapDefinitions
                : Array.Empty<PollDefinitionDto>().AsReadOnly();

            var info = new DeviceInfo(d.Name, d.IpAddress, d.DeviceType, trapDefinitions);
            _devices[ip] = info;
            _devicesByName[info.Name] = info;
        }

        // Auto-register virtual device modules (e.g., SimetraModule loopback).
        // Config devices win: skip if a config device already occupies the IP.
        foreach (var vm in modules.OfType<IVirtualDeviceModule>())
        {
            var ip = IPAddress.Parse(vm.VirtualDeviceIpAddress).MapToIPv4();
            if (_devices.ContainsKey(ip))
                continue;

            var info = new DeviceInfo(vm.VirtualDeviceName, vm.VirtualDeviceIpAddress, vm.DeviceType, vm.TrapDefinitions);
            _devices[ip] = info;
            _devicesByName[info.Name] = info;
        }
    }

    /// <inheritdoc />
    public bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device)
    {
        return _devices.TryGetValue(senderIp.MapToIPv4(), out device);
    }

    /// <inheritdoc />
    public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
    {
        return _devicesByName.TryGetValue(deviceName, out device);
    }
}
