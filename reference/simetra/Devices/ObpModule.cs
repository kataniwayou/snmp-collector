using Simetra.Configuration;
using Simetra.Models;

namespace Simetra.Devices;

/// <summary>
/// OBP bypass device module. Defines 4 trap definitions (linkN_StateChange for links 1-4),
/// 2 enum maps (LinkStateEnumMap, ChannelEnumMap), and 8 state poll definitions
/// (linkN_State x4 + linkN_Channel x4).
///
/// All OIDs derive from the BYPASS-CGS.mib hierarchy rooted at
/// enterprises(1) > cgs(47477) > EBP-1U2U4U(10) > bypass(21).
/// </summary>
public sealed class ObpModule : IDeviceModule
{
    // --- OID Constants (derived from BYPASS-CGS.mib) ---

    /// <summary>
    /// Base OID prefix for the OBP bypass device:
    /// enterprises.cgs(47477).EBP-1U2U4U(10).bypass(21)
    /// </summary>
    private const string BypassPrefix = "1.3.6.1.4.1.47477.10.21";

    // Per-link OBP prefixes (linkN = bypass.N, linkNOBP = linkN.3)
    private const string Link1OBPPrefix = BypassPrefix + ".1.3";
    private const string Link2OBPPrefix = BypassPrefix + ".2.3";
    private const string Link3OBPPrefix = BypassPrefix + ".3.3";
    private const string Link4OBPPrefix = BypassPrefix + ".4.3";

    // Per-link trap prefixes (linkNOBPTrap = linkNOBP.50)
    private const string Link1TrapPrefix = Link1OBPPrefix + ".50";
    private const string Link2TrapPrefix = Link2OBPPrefix + ".50";
    private const string Link3TrapPrefix = Link3OBPPrefix + ".50";
    private const string Link4TrapPrefix = Link4OBPPrefix + ".50";

    // Per-link state poll OIDs (linkN_State = linkNOBP.1.0)
    private const string Link1StateOid = Link1OBPPrefix + ".1.0";
    private const string Link2StateOid = Link2OBPPrefix + ".1.0";
    private const string Link3StateOid = Link3OBPPrefix + ".1.0";
    private const string Link4StateOid = Link4OBPPrefix + ".1.0";

    // Per-link channel poll OIDs (linkN_Channel = linkNOBP.4.0)
    private const string Link1ChannelOid = Link1OBPPrefix + ".4.0";
    private const string Link2ChannelOid = Link2OBPPrefix + ".4.0";
    private const string Link3ChannelOid = Link3OBPPrefix + ".4.0";
    private const string Link4ChannelOid = Link4OBPPrefix + ".4.0";

    // Per-link StateChange trap OIDs (linkNOBPTrap.2)
    public const string Link1StateChangeTrapOid = Link1TrapPrefix + ".2";
    public const string Link2StateChangeTrapOid = Link2TrapPrefix + ".2";
    public const string Link3StateChangeTrapOid = Link3TrapPrefix + ".2";
    public const string Link4StateChangeTrapOid = Link4TrapPrefix + ".2";

    // --- Enum Maps ---

    private static readonly IReadOnlyDictionary<int, string> LinkStateEnumMap =
        new Dictionary<int, string>
        {
            { 0, "Off" },
            { 1, "On" }
        }.AsReadOnly();

    private static readonly IReadOnlyDictionary<int, string> ChannelEnumMap =
        new Dictionary<int, string>
        {
            { 0, "Bypass" },
            { 1, "Primary" }
        }.AsReadOnly();

    // --- IDeviceModule Implementation ---

    /// <inheritdoc />
    public string DeviceType => "OBP";

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; } = new List<PollDefinitionDto>
    {
        new PollDefinitionDto(
            MetricName: "state_change",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link1StateChangeTrapOid, "state_change", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "1" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "state_change",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link2StateChangeTrapOid, "state_change", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "2" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "state_change",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link3StateChangeTrapOid, "state_change", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "3" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "state_change",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link4StateChangeTrapOid, "state_change", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "4" } }.AsReadOnly()),
    }.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; } = new List<PollDefinitionDto>
    {
        new PollDefinitionDto(
            MetricName: "link_state",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link1StateOid, "link_state", OidRole.Metric, LinkStateEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "1" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "link_state",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link2StateOid, "link_state", OidRole.Metric, LinkStateEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "2" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "link_state",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link3StateOid, "link_state", OidRole.Metric, LinkStateEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "3" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "link_state",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link4StateOid, "link_state", OidRole.Metric, LinkStateEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "4" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "link_channel",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link1ChannelOid, "link_channel", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "1" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "link_channel",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link2ChannelOid, "link_channel", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "2" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "link_channel",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link3ChannelOid, "link_channel", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "3" } }.AsReadOnly()),

        new PollDefinitionDto(
            MetricName: "link_channel",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Link4ChannelOid, "link_channel", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "link_number", "4" } }.AsReadOnly()),
    }.AsReadOnly();
}
