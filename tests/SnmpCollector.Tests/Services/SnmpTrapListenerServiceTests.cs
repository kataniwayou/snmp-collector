using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Services;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Services;

/// <summary>
/// Unit tests for the ProcessDatagram internal method of <see cref="SnmpTrapListenerService"/>.
/// Verifies auth failure, unknown device drop, successful routing, and correct VarbindEnvelope fields.
///
/// Placed in NonParallelMeterTests collection to prevent cross-test meter contamination
/// (MeterListener is a global listener; parallel tests with the same meter name interfere).
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class SnmpTrapListenerServiceTests : IDisposable
{
    private const string KnownDeviceName = "test-router";
    private const string KnownDeviceIp = "10.0.1.1";
    private const string CorrectCommunity = "secret";

    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;
    private readonly MeterListener _meterListener;
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public SnmpTrapListenerServiceTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _metrics = new PipelineMetricService(
            _sp.GetRequiredService<IMeterFactory>(),
            Options.Create(new SiteOptions { Name = "test-site" }));

        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            _measurements.Add((instrument.Name, value, tags.ToArray()));
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _metrics.Dispose();
        _sp.Dispose();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static DeviceInfo KnownDevice()
        => new DeviceInfo(KnownDeviceName, KnownDeviceIp, CorrectCommunity, []);

    /// <summary>Build a SNMPv2c trap byte buffer with one varbind.</summary>
    private static byte[] BuildTrapBytes(string community, string oid = "1.3.6.1.2.1.1.1.0")
    {
        var varbinds = new List<Variable>
        {
            new Variable(new ObjectIdentifier(oid), new Integer32(42))
        };
        var msg = new TrapV2Message(
            requestId: 1,
            VersionCode.V2,
            new OctetString(community),
            new ObjectIdentifier("1.3.6.1.6.3.1.1.5.1"),
            time: 0,
            varbinds);
        return msg.ToBytes();
    }

    private SnmpTrapListenerService CreateListener(IDeviceRegistry registry, IDeviceChannelManager? channelManager = null)
    {
        channelManager ??= new NoOpChannelManager();

        return new SnmpTrapListenerService(
            registry,
            channelManager,
            _metrics,
            Options.Create(new SnmpListenerOptions
            {
                BindAddress = "0.0.0.0",
                Port = 10162,
                CommunityString = CorrectCommunity,
                Version = "v2c"
            }),
            NullLogger<SnmpTrapListenerService>.Instance);
    }

    private static UdpReceiveResult MakeResult(byte[] bytes, string fromIp)
        => new UdpReceiveResult(bytes, new IPEndPoint(IPAddress.Parse(fromIp), 12345));

    // -----------------------------------------------------------------------
    // 1. UnknownDevice_DropsAndIncrementsCounter
    // -----------------------------------------------------------------------

    [Fact]
    public void UnknownDevice_DropsAndIncrementsCounter()
    {
        // Registry with no devices — every IP is unknown
        var registry = new StubDeviceRegistry([]);
        var listener = CreateListener(registry);

        var bytes = BuildTrapBytes(CorrectCommunity);
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        var unknownDeviceCount = _measurements
            .Count(m => m.InstrumentName == "snmp.trap.unknown_device");
        Assert.Equal(1, unknownDeviceCount);

        // auth_failed must NOT be incremented (device not found = no auth step reached)
        var authFailedCount = _measurements
            .Count(m => m.InstrumentName == "snmp.trap.auth_failed");
        Assert.Equal(0, authFailedCount);
    }

    // -----------------------------------------------------------------------
    // 2. WrongCommunity_DropsAndIncrementsAuthFailed
    // -----------------------------------------------------------------------

    [Fact]
    public void WrongCommunity_DropsAndIncrementsAuthFailed()
    {
        var registry = new StubDeviceRegistry([KnownDevice()]);
        var listener = CreateListener(registry);

        // Use wrong community string
        var bytes = BuildTrapBytes("wrong-community");
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        var authFailedCount = _measurements
            .Count(m => m.InstrumentName == "snmp.trap.auth_failed");
        Assert.Equal(1, authFailedCount);

        // unknown_device must NOT be incremented (device was found, only community string wrong)
        var unknownCount = _measurements
            .Count(m => m.InstrumentName == "snmp.trap.unknown_device");
        Assert.Equal(0, unknownCount);
    }

    // -----------------------------------------------------------------------
    // 3. AuthenticatedTrap_WritesVarbindEnvelopesToChannel
    // -----------------------------------------------------------------------

    [Fact]
    public void AuthenticatedTrap_WritesVarbindEnvelopesToChannel()
    {
        var registry = new StubDeviceRegistry([KnownDevice()]);
        var channelManager = new CapturingChannelManager(KnownDeviceName);
        var listener = CreateListener(registry, channelManager);

        var bytes = BuildTrapBytes(CorrectCommunity, "1.3.6.1.2.1.2.2.1.10.1");
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        Assert.Single(channelManager.Written);
        Assert.Equal("1.3.6.1.2.1.2.2.1.10.1", channelManager.Written[0].Oid);
    }

    // -----------------------------------------------------------------------
    // 4. VarbindEnvelope_HasCorrectFields
    // -----------------------------------------------------------------------

    [Fact]
    public void VarbindEnvelope_HasCorrectFields()
    {
        var registry = new StubDeviceRegistry([KnownDevice()]);
        var channelManager = new CapturingChannelManager(KnownDeviceName);
        var listener = CreateListener(registry, channelManager);

        var bytes = BuildTrapBytes(CorrectCommunity, "1.3.6.1.2.1.1.3.0");
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        Assert.Single(channelManager.Written);
        var envelope = channelManager.Written[0];

        Assert.Equal("1.3.6.1.2.1.1.3.0", envelope.Oid);
        Assert.Equal(KnownDeviceName, envelope.DeviceName);
        Assert.Equal(IPAddress.Parse(KnownDeviceIp), envelope.AgentIp);
        Assert.Equal(SnmpType.Integer32, envelope.TypeCode);
    }

    // -----------------------------------------------------------------------
    // 5. MalformedPacket_DoesNotThrow_AndDropsQuietly
    // -----------------------------------------------------------------------

    [Fact]
    public void MalformedPacket_DoesNotThrow_AndDropsQuietly()
    {
        var registry = new StubDeviceRegistry([KnownDevice()]);
        var listener = CreateListener(registry);

        // Random garbage bytes that are not valid SNMP
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE };
        var result = MakeResult(garbage, KnownDeviceIp);

        // Should not throw
        var ex = Record.Exception(() => listener.ProcessDatagram(result));
        Assert.Null(ex);

        // No trap counter measurements should be recorded
        Assert.DoesNotContain(_measurements, m =>
            m.InstrumentName is "snmp.trap.auth_failed" or "snmp.trap.unknown_device");
    }

    // -----------------------------------------------------------------------
    // Stubs
    // -----------------------------------------------------------------------

    private sealed class StubDeviceRegistry : IDeviceRegistry
    {
        private readonly IReadOnlyList<DeviceInfo> _devices;

        public StubDeviceRegistry(IReadOnlyList<DeviceInfo> devices) => _devices = devices;

        public IReadOnlyList<DeviceInfo> AllDevices => _devices;

        public bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _devices.FirstOrDefault(d =>
                IPAddress.Parse(d.IpAddress).MapToIPv4().Equals(senderIp.MapToIPv4()));
            return device is not null;
        }

        public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _devices.FirstOrDefault(d =>
                string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
            return device is not null;
        }
    }

    /// <summary>Channel manager that captures written envelopes for a single device.</summary>
    private sealed class CapturingChannelManager : IDeviceChannelManager
    {
        public List<VarbindEnvelope> Written { get; } = new();
        public string DeviceName { get; }

        public CapturingChannelManager(string deviceName)
        {
            DeviceName = deviceName;
        }

        public System.Threading.Channels.ChannelWriter<VarbindEnvelope> GetWriter(string name)
            => new CapturingWriter(Written);

        public System.Threading.Channels.ChannelReader<VarbindEnvelope> GetReader(string name)
            => System.Threading.Channels.Channel.CreateUnbounded<VarbindEnvelope>().Reader;

        public IReadOnlyCollection<string> DeviceNames => [DeviceName];

        public void CompleteAll() { }

        public Task WaitForDrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class CapturingWriter : System.Threading.Channels.ChannelWriter<VarbindEnvelope>
        {
            private readonly List<VarbindEnvelope> _list;
            public CapturingWriter(List<VarbindEnvelope> list) => _list = list;

            public override bool TryWrite(VarbindEnvelope item)
            {
                _list.Add(item);
                return true;
            }

            public override System.Threading.Tasks.ValueTask<bool> WaitToWriteAsync(
                System.Threading.CancellationToken cancellationToken = default)
                => System.Threading.Tasks.ValueTask.FromResult(true);
        }
    }

    /// <summary>Channel manager that silently discards all writes (for tests that only check metrics).</summary>
    private sealed class NoOpChannelManager : IDeviceChannelManager
    {
        public System.Threading.Channels.ChannelWriter<VarbindEnvelope> GetWriter(string name)
            => new NoOpWriter();

        public System.Threading.Channels.ChannelReader<VarbindEnvelope> GetReader(string name)
            => System.Threading.Channels.Channel.CreateUnbounded<VarbindEnvelope>().Reader;

        public IReadOnlyCollection<string> DeviceNames => [];

        public void CompleteAll() { }

        public Task WaitForDrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class NoOpWriter : System.Threading.Channels.ChannelWriter<VarbindEnvelope>
        {
            public override bool TryWrite(VarbindEnvelope item) => true;

            public override System.Threading.Tasks.ValueTask<bool> WaitToWriteAsync(
                System.Threading.CancellationToken cancellationToken = default)
                => System.Threading.Tasks.ValueTask.FromResult(true);
        }
    }
}
