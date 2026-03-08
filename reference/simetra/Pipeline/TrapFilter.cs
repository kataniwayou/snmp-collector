using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Stateless singleton that matches trap varbind OIDs against a device's poll definitions.
/// Returns the first <see cref="PollDefinitionDto"/> whose OIDs intersect with the varbinds.
/// </summary>
public sealed class TrapFilter : ITrapFilter
{
    private readonly ILogger<TrapFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrapFilter"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public TrapFilter(ILogger<TrapFilter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public PollDefinitionDto? Match(IList<Variable> varbinds, DeviceInfo device)
    {
        foreach (var definition in device.TrapDefinitions)
        {
            var definitionOids = new HashSet<string>(
                definition.Oids.Select(o => o.Oid),
                StringComparer.Ordinal);

            foreach (var varbind in varbinds)
            {
                if (definitionOids.Contains(varbind.Id.ToString()))
                {
                    return definition;
                }
            }
        }

        _logger.LogDebug(
            "No matching trap definition for varbinds from device {DeviceName}",
            device.Name);

        return null;
    }
}
