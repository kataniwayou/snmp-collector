using Lextm.SharpSnmpLib;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Filters incoming trap varbinds against a device's poll definitions to find the
/// first matching definition by OID intersection.
/// </summary>
public interface ITrapFilter
{
    /// <summary>
    /// Returns the first <see cref="PollDefinitionDto"/> whose OIDs match any of the
    /// varbind OIDs, or null if no definition matches.
    /// </summary>
    /// <param name="varbinds">SNMP varbinds from the received trap.</param>
    /// <param name="device">The device whose trap definitions to match against.</param>
    /// <returns>The matched poll definition, or null if no match is found.</returns>
    PollDefinitionDto? Match(IList<Variable> varbinds, DeviceInfo device);
}
