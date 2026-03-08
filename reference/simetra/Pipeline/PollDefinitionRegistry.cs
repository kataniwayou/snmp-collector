using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Devices;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Singleton registry that indexes all poll definitions by a composite
/// <c>"deviceName::metricName"</c> key for O(1) lookup. Built once at startup from
/// device modules (state polls) and configuration (metric polls).
/// Module definitions take precedence over configuration on key collision;
/// duplicate configuration MetricNames within the same device keep only the first.
/// </summary>
public sealed class PollDefinitionRegistry : IPollDefinitionRegistry
{
    private readonly Dictionary<string, PollDefinitionDto> _definitions;
    private readonly List<(string DeviceName, PollDefinitionDto Definition)> _statePollDefinitions;
    private readonly List<(string DeviceName, PollDefinitionDto Definition)> _metricPollDefinitions;

    /// <summary>
    /// Initializes the registry by indexing all poll definitions from modules and configuration.
    /// Module <see cref="IDeviceModule.StatePollDefinitions"/> are applied to every config device
    /// whose <see cref="DeviceOptions.DeviceType"/> matches the module's <see cref="IDeviceModule.DeviceType"/>.
    /// Configuration <see cref="DeviceOptions.MetricPolls"/> become metric poll entries.
    /// Module definitions win on key collision; duplicate config MetricNames keep first only.
    /// </summary>
    /// <param name="devicesOptions">The configured devices providing metric poll definitions.</param>
    /// <param name="modules">Code-defined device modules providing type-level state poll definitions.</param>
    /// <param name="logger">Logger for reporting skipped duplicates.</param>
    public PollDefinitionRegistry(
        IOptions<DevicesOptions> devicesOptions,
        IEnumerable<IDeviceModule> modules,
        ILogger<PollDefinitionRegistry> logger)
    {
        _definitions = new Dictionary<string, PollDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        _statePollDefinitions = new List<(string, PollDefinitionDto)>();
        _metricPollDefinitions = new List<(string, PollDefinitionDto)>();

        // Index modules by DeviceType for O(1) lookup
        var modulesByType = modules.ToDictionary(m => m.DeviceType, StringComparer.OrdinalIgnoreCase);

        // Apply module state poll definitions to each config device by DeviceType.
        // Uses PollKey (MetricName + StaticLabels) for uniqueness -- e.g., fan_status-1, fan_status-2.
        foreach (var device in devicesOptions.Value.Devices)
        {
            if (modulesByType.TryGetValue(device.DeviceType, out var module))
            {
                foreach (var def in module.StatePollDefinitions)
                {
                    var key = $"{device.Name}::{def.PollKey}";
                    _definitions[key] = def;
                    _statePollDefinitions.Add((device.Name, def));
                }
            }
        }

        // Index metric poll definitions from configuration (Source=Configuration).
        // Config polls have unique MetricNames per device, so PollKey == MetricName.
        // Module definitions win on key collision; duplicate config MetricNames keep first only.
        foreach (var device in devicesOptions.Value.Devices)
        {
            foreach (var poll in device.MetricPolls)
            {
                var def = PollDefinitionDto.FromOptions(poll);
                var key = $"{device.Name}::{def.PollKey}";

                if (!_definitions.TryAdd(key, def))
                {
                    var existing = _definitions[key];
                    logger.LogWarning(
                        "Skipping config poll {MetricName} for device {DeviceName}: " +
                        "already defined by {Source}",
                        def.PollKey, device.Name, existing.Source);
                    continue;
                }

                _metricPollDefinitions.Add((device.Name, def));
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetDefinition(string deviceName, string pollKey, [NotNullWhen(true)] out PollDefinitionDto? definition)
    {
        var key = $"{deviceName}::{pollKey}";
        return _definitions.TryGetValue(key, out definition);
    }

    /// <inheritdoc />
    public IReadOnlyList<(string DeviceName, PollDefinitionDto Definition)> GetAllStatePollDefinitions()
    {
        return _statePollDefinitions.AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<(string DeviceName, PollDefinitionDto Definition)> GetAllMetricPollDefinitions()
    {
        return _metricPollDefinitions.AsReadOnly();
    }
}
