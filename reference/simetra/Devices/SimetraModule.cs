using Simetra.Configuration;
using Simetra.Models;

namespace Simetra.Devices;

/// <summary>
/// Simetra virtual device module for heartbeat loopback. Sends periodic heartbeat traps
/// to itself (127.0.0.1) to prove the SNMP pipeline is alive. The heartbeat trap definition
/// flows through the same generic pipeline infrastructure as any external device -- no
/// special-case code exists for Simetra anywhere in the pipeline.
/// </summary>
public sealed class SimetraModule : IVirtualDeviceModule
{
    /// <inheritdoc />
    public string VirtualDeviceName => "simetra-supervisor";

    /// <inheritdoc />
    public string VirtualDeviceIpAddress => "127.0.0.1";

    /// <summary>
    /// Single source of truth for the heartbeat OID. Consumed by Phase 6 HeartbeatJob
    /// to construct the outgoing trap varbind, ensuring the OID in the trap matches the
    /// OID in the trap definition without string duplication.
    /// </summary>
    public const string HeartbeatOid = "1.3.6.1.4.1.9999.1.1.1.0";

    /// <inheritdoc />
    public string DeviceType => "simetra";

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; } = new List<PollDefinitionDto>
    {
        new PollDefinitionDto(
            MetricName: "simetra_heartbeat",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(
                    Oid: HeartbeatOid,
                    PropertyName: "beat",
                    Role: OidRole.Metric,
                    EnumMap: null)
            }.AsReadOnly(),
            IntervalSeconds: 15,
            Source: MetricPollSource.Module)
    }.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; } =
        new List<PollDefinitionDto>().AsReadOnly();
}
