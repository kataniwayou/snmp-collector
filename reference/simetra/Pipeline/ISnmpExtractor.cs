using Lextm.SharpSnmpLib;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Extracts SNMP varbind data into an <see cref="ExtractionResult"/> using
/// <see cref="PollDefinitionDto"/> instructions. Same logic for traps and polls --
/// no per-device-type logic.
/// </summary>
public interface ISnmpExtractor
{
    /// <summary>
    /// Extracts metrics, labels, and enum-map metadata from SNMP varbinds according
    /// to the provided poll definition.
    /// </summary>
    /// <param name="varbinds">SNMP varbinds from trap or poll response.</param>
    /// <param name="definition">Poll definition with OID entries and roles.</param>
    /// <returns>Extraction result with metrics, labels, and enum-map metadata.</returns>
    ExtractionResult Extract(IList<Variable> varbinds, PollDefinitionDto definition);
}
