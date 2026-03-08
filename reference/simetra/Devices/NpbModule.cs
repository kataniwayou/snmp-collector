using Simetra.Configuration;
using Simetra.Models;

namespace Simetra.Devices;

/// <summary>
/// NPB-2E reference device module for a standard SNMP device with NOTIFICATION-TYPE traps
/// and table-based port statistics. Defines 7 trap definitions covering the required NPB trap
/// types (portLinkUp/Down, portUtilizationRx/Tx, psuPower, psuTemperature, inlineToolStatusChange),
/// 22 module-source state polls (2 inlineToolStatus + 1 sys_uptime + 1 cpu_load_1min +
/// 1 mem_total + 1 mem_available + 8 port_rx_octets + 8 port_tx_octets), and 0 EnumMaps
/// (all 22 polls use raw numeric values). All OIDs are derived from the NPB MIB hierarchy
/// rooted at enterprises(1) > cgs(47477) > npb(100) > npb-2e(4).
///
/// Technical note: One trap definition per trap type (7 total) rather than per-port (28)
/// because TrapFilter.Match() returns the first matching definition by OID intersection,
/// and all per-port variants share identical bare column varbind OIDs. The port_number is
/// extracted dynamically as a Label from the portsPortLogicalPortNumber varbind value.
/// </summary>
public sealed class NpbModule : IDeviceModule
{
    // --- OID Constants (single source of truth, derived from MIB hierarchy) ---

    /// <summary>
    /// Base OID prefix for the NPB-2E device: enterprises.cgs(47477).npb(100).npb-2e(4).
    /// </summary>
    private const string Npb2ePrefix = "1.3.6.1.4.1.47477.100.4";

    // ---- Trap notification OIDs (public -- needed for trap matching by notification OID) ----

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for portLinkUp (notifications.101).
    /// </summary>
    public const string PortLinkUpTrapOid = Npb2ePrefix + ".10.2.101";

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for portLinkDown (notifications.102).
    /// </summary>
    public const string PortLinkDownTrapOid = Npb2ePrefix + ".10.2.102";

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for portUtilizationRx (notifications.121).
    /// </summary>
    public const string PortUtilizationRxTrapOid = Npb2ePrefix + ".10.2.121";

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for portUtilizationTx (notifications.122).
    /// </summary>
    public const string PortUtilizationTxTrapOid = Npb2ePrefix + ".10.2.122";

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for PSU power (notifications.202).
    /// </summary>
    public const string PsuPowerTrapOid = Npb2ePrefix + ".10.2.202";

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for PSU temperature (notifications.203).
    /// </summary>
    public const string PsuTemperatureTrapOid = Npb2ePrefix + ".10.2.203";

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for inline tool status change (notifications.451).
    /// </summary>
    public const string InlineToolStatusChangeTrapOid = Npb2ePrefix + ".10.2.451";

    // ---- Common trap variable OIDs (varbinds carried in all NPB trap PDUs) ----

    private const string ModuleOid   = Npb2ePrefix + ".10.1.1"; // variables.1
    private const string SeverityOid = Npb2ePrefix + ".10.1.2"; // variables.2
    private const string TypeOid     = Npb2ePrefix + ".10.1.3"; // variables.3
    private const string MessageOid  = Npb2ePrefix + ".10.1.4"; // variables.4

    // ---- Port entry OIDs (portsPortEntry columns -- bare column OIDs for trap varbind matching) ----

    private const string PortEntryPrefix          = Npb2ePrefix + ".2.1.4.1";
    private const string PortLogicalPortNumberOid = PortEntryPrefix + ".1"; // portsPortEntry.1
    private const string PortLinkStatusOid        = PortEntryPrefix + ".3"; // portsPortEntry.3
    private const string PortSpeedOid             = PortEntryPrefix + ".4"; // portsPortEntry.4

    // ---- Port utilization varbind OIDs (bare column OIDs for Rx/Tx utilization traps) ----

    private const string RxUtilBps1secOid    = Npb2ePrefix + ".2.2.4.1.1.8";  // rxUtilBpsCurrent1sec
    private const string RxUtilPct1secOid    = Npb2ePrefix + ".2.2.4.1.1.7";  // rxUtilPercentsCurrent1sec
    private const string RxRaiseThresholdOid = PortEntryPrefix + ".25";        // rxRaiseThreshold
    private const string TxUtilBps1secOid    = Npb2ePrefix + ".2.2.4.1.1.19"; // txUtilBpsCurrent1sec
    private const string TxUtilPct1secOid    = Npb2ePrefix + ".2.2.4.1.1.18"; // txUtilPercentsCurrent1sec
    private const string TxRaiseThresholdOid = PortEntryPrefix + ".28";        // txRaiseThreshold

    // ---- PSU alarm varbind OIDs ----

    private const string PsuEntryPrefix = Npb2ePrefix + ".1.1.13.2.1";
    private const string PsuNumberOid   = PsuEntryPrefix + ".1"; // psuNumber
    private const string PsuVoltageOid  = PsuEntryPrefix + ".5"; // psuVoltage
    private const string PsuTempOid     = PsuEntryPrefix + ".8"; // psuTemp

    // ---- Inline tool varbind OIDs ----
    private const string InlineToolEntryPrefix       = Npb2ePrefix + ".7.1.1.1";
    private const string InlineToolNameOid           = InlineToolEntryPrefix + ".1"; // inlineToolName
    private const string InlineToolPortAOid          = InlineToolEntryPrefix + ".3"; // inlineToolPortA
    private const string InlineToolPortBOid          = InlineToolEntryPrefix + ".4"; // inlineToolPortB
    private const string InlineToolFailoverActionOid = InlineToolEntryPrefix + ".5"; // inlineToolFailoverAction

    // ---- Port statistics summary OIDs (bare column OIDs -- used by port octet polls) ----

    private const string SummaryEntryPrefix = Npb2ePrefix + ".2.2.5.1.1";

    // ---- System Details poll OIDs ----
    private const string SysUptimeOid = Npb2ePrefix + ".1.1.12.12.0";

    // ---- CPU Load poll OIDs ----
    private const string CpuLoadPrefix  = Npb2ePrefix + ".1.1.15.1.1";
    private const string CpuLoad1minOid = CpuLoadPrefix + ".1.0";

    // ---- Memory poll OIDs ----
    private const string MemoryPrefix    = Npb2ePrefix + ".1.1.15.2";
    private const string MemTotalOid     = MemoryPrefix + ".1.0";
    private const string MemAvailableOid = MemoryPrefix + ".3.0";

    // ---- Port statistics summary - octet poll OIDs (ports 1-8, column.port.0) ----
    private const string RxOctetsPollOidP1 = SummaryEntryPrefix + ".3.1.0";
    private const string RxOctetsPollOidP2 = SummaryEntryPrefix + ".3.2.0";
    private const string RxOctetsPollOidP3 = SummaryEntryPrefix + ".3.3.0";
    private const string RxOctetsPollOidP4 = SummaryEntryPrefix + ".3.4.0";
    private const string RxOctetsPollOidP5 = SummaryEntryPrefix + ".3.5.0";
    private const string RxOctetsPollOidP6 = SummaryEntryPrefix + ".3.6.0";
    private const string RxOctetsPollOidP7 = SummaryEntryPrefix + ".3.7.0";
    private const string RxOctetsPollOidP8 = SummaryEntryPrefix + ".3.8.0";

    private const string TxOctetsPollOidP1 = SummaryEntryPrefix + ".4.1.0";
    private const string TxOctetsPollOidP2 = SummaryEntryPrefix + ".4.2.0";
    private const string TxOctetsPollOidP3 = SummaryEntryPrefix + ".4.3.0";
    private const string TxOctetsPollOidP4 = SummaryEntryPrefix + ".4.4.0";
    private const string TxOctetsPollOidP5 = SummaryEntryPrefix + ".4.5.0";
    private const string TxOctetsPollOidP6 = SummaryEntryPrefix + ".4.6.0";
    private const string TxOctetsPollOidP7 = SummaryEntryPrefix + ".4.7.0";
    private const string TxOctetsPollOidP8 = SummaryEntryPrefix + ".4.8.0";

    // ---- Inline tool status poll OIDs (string-indexed: tool name "1" = ASCII 49, "2" = ASCII 50) ----
    private const string InlineToolStatus1PollOid = InlineToolEntryPrefix + ".7.1.49";
    private const string InlineToolStatus2PollOid = InlineToolEntryPrefix + ".7.1.50";

    // --- IDeviceModule Implementation ---

    /// <inheritdoc />
    public string DeviceType => "NPB";

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; } = new List<PollDefinitionDto>
    {
        // portLinkUp trap (NPB-02): 6 varbinds
        new PollDefinitionDto(
            MetricName: "port_link_up",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid, "module", OidRole.Label, null),
                new OidEntryDto(SeverityOid, "severity", OidRole.Label, null),
                new OidEntryDto(TypeOid, "type", OidRole.Label, null),
                new OidEntryDto(MessageOid, "message", OidRole.Label, null),
                new OidEntryDto(PortLogicalPortNumberOid, "port_number", OidRole.Label, null),
                new OidEntryDto(PortSpeedOid, "port_speed", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // portLinkDown trap (NPB-03): 5 varbinds
        new PollDefinitionDto(
            MetricName: "port_link_down",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid, "module", OidRole.Label, null),
                new OidEntryDto(SeverityOid, "severity", OidRole.Label, null),
                new OidEntryDto(TypeOid, "type", OidRole.Label, null),
                new OidEntryDto(MessageOid, "message", OidRole.Label, null),
                new OidEntryDto(PortLogicalPortNumberOid, "port_number", OidRole.Label, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // portUtilizationRx trap: 8 varbinds
        // DisplayString varbinds use OidRole.Metric -- parsed as double by SnmpExtractorService (Phase 31)
        new PollDefinitionDto(
            MetricName: "port_utilization_rx",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid, "module", OidRole.Label, null),
                new OidEntryDto(SeverityOid, "severity", OidRole.Label, null),
                new OidEntryDto(TypeOid, "type", OidRole.Label, null),
                new OidEntryDto(MessageOid, "message", OidRole.Label, null),
                new OidEntryDto(PortLogicalPortNumberOid, "port_number", OidRole.Label, null),
                new OidEntryDto(RxUtilBps1secOid, "rx_util_bps_1sec", OidRole.Metric, null),
                new OidEntryDto(RxUtilPct1secOid, "rx_util_pct_1sec", OidRole.Metric, null),
                new OidEntryDto(RxRaiseThresholdOid, "rx_raise_threshold", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // portUtilizationTx trap: 8 varbinds
        new PollDefinitionDto(
            MetricName: "port_utilization_tx",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid, "module", OidRole.Label, null),
                new OidEntryDto(SeverityOid, "severity", OidRole.Label, null),
                new OidEntryDto(TypeOid, "type", OidRole.Label, null),
                new OidEntryDto(MessageOid, "message", OidRole.Label, null),
                new OidEntryDto(PortLogicalPortNumberOid, "port_number", OidRole.Label, null),
                new OidEntryDto(TxUtilBps1secOid, "tx_util_bps_1sec", OidRole.Metric, null),
                new OidEntryDto(TxUtilPct1secOid, "tx_util_pct_1sec", OidRole.Metric, null),
                new OidEntryDto(TxRaiseThresholdOid, "tx_raise_threshold", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // psu_power trap: 7 varbinds (4 header + psu_number, psu_voltage, psu_temp)
        new PollDefinitionDto(
            MetricName: "psu_power",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid,   "module",      OidRole.Label,  null),
                new OidEntryDto(SeverityOid, "severity",    OidRole.Label,  null),
                new OidEntryDto(TypeOid,     "type",        OidRole.Label,  null),
                new OidEntryDto(MessageOid,  "message",     OidRole.Label,  null),
                new OidEntryDto(PsuNumberOid, "psu_number", OidRole.Label,  null),
                new OidEntryDto(PsuVoltageOid, "psu_voltage", OidRole.Metric, null),
                new OidEntryDto(PsuTempOid,  "psu_temp",    OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // psu_temperature trap: 7 varbinds (4 header + psu_number, psu_voltage, psu_temp)
        new PollDefinitionDto(
            MetricName: "psu_temperature",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid,   "module",      OidRole.Label,  null),
                new OidEntryDto(SeverityOid, "severity",    OidRole.Label,  null),
                new OidEntryDto(TypeOid,     "type",        OidRole.Label,  null),
                new OidEntryDto(MessageOid,  "message",     OidRole.Label,  null),
                new OidEntryDto(PsuNumberOid, "psu_number", OidRole.Label,  null),
                new OidEntryDto(PsuVoltageOid, "psu_voltage", OidRole.Metric, null),
                new OidEntryDto(PsuTempOid,  "psu_temp",    OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // inline_tool_status_change trap: 8 varbinds (4 header + tool_name, port_a, port_b, failover_action)
        new PollDefinitionDto(
            MetricName: "inline_tool_status_change",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid,   "module",          OidRole.Label, null),
                new OidEntryDto(SeverityOid, "severity",        OidRole.Label, null),
                new OidEntryDto(TypeOid,     "type",            OidRole.Label, null),
                new OidEntryDto(MessageOid,  "message",         OidRole.Label, null),
                new OidEntryDto(InlineToolNameOid,           "tool_name",       OidRole.Label, null),
                new OidEntryDto(InlineToolPortAOid,          "port_a",          OidRole.Label, null),
                new OidEntryDto(InlineToolPortBOid,          "port_b",          OidRole.Label, null),
                new OidEntryDto(InlineToolFailoverActionOid, "failover_action", OidRole.Label, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module)
    }.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; } = new List<PollDefinitionDto>
    {
        // ---- Inline Tool Status (2 polls, Gauge, 60s) ----
        new PollDefinitionDto(
            MetricName: "inline_tool_status",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(InlineToolStatus1PollOid, "inline_tool_status", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 60,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "tool_id", "1" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "inline_tool_status",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(InlineToolStatus2PollOid, "inline_tool_status", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 60,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "tool_id", "2" } }.AsReadOnly()),

        // ---- System Uptime (1 poll, Gauge, 60s) ----
        new PollDefinitionDto(
            MetricName: "sys_uptime",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(SysUptimeOid, "sys_uptime", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 60,
            Source: MetricPollSource.Module),

        // ---- CPU Load (1 poll, Gauge, 60s) ----
        new PollDefinitionDto(
            MetricName: "cpu_load_1min",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(CpuLoad1minOid, "cpu_load_1min", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 60,
            Source: MetricPollSource.Module),

        // ---- Memory (2 polls, Gauge, 60s) ----
        new PollDefinitionDto(
            MetricName: "mem_total",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(MemTotalOid, "mem_total", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 60,
            Source: MetricPollSource.Module),
        new PollDefinitionDto(
            MetricName: "mem_available",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(MemAvailableOid, "mem_available", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 60,
            Source: MetricPollSource.Module),

        // ---- Port Rx Octets (8 polls, Counter, 30s) ----
        new PollDefinitionDto(
            MetricName: "port_rx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxOctetsPollOidP1, "port_rx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "1" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_rx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxOctetsPollOidP2, "port_rx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "2" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_rx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxOctetsPollOidP3, "port_rx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "3" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_rx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxOctetsPollOidP4, "port_rx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "4" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_rx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxOctetsPollOidP5, "port_rx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "5" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_rx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxOctetsPollOidP6, "port_rx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "6" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_rx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxOctetsPollOidP7, "port_rx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "7" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_rx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxOctetsPollOidP8, "port_rx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "8" } }.AsReadOnly()),

        // ---- Port Tx Octets (8 polls, Counter, 30s) ----
        new PollDefinitionDto(
            MetricName: "port_tx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxOctetsPollOidP1, "port_tx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "1" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_tx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxOctetsPollOidP2, "port_tx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "2" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_tx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxOctetsPollOidP3, "port_tx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "3" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_tx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxOctetsPollOidP4, "port_tx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "4" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_tx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxOctetsPollOidP5, "port_tx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "5" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_tx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxOctetsPollOidP6, "port_tx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "6" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_tx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxOctetsPollOidP7, "port_tx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "7" } }.AsReadOnly()),
        new PollDefinitionDto(
            MetricName: "port_tx_octets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxOctetsPollOidP8, "port_tx_octets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module,
            StaticLabels: new Dictionary<string, string> { { "port_number", "8" } }.AsReadOnly())
    }.AsReadOnly();
}
