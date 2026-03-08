using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Pipeline;
using Simetra.Telemetry;

namespace Simetra.Services;

/// <summary>
/// BackgroundService that receives SNMP v2c traps via UDP, orchestrating Layer 1 (reception)
/// and Layer 2 (device routing, OID filtering, channel write). Each datagram is parsed with
/// <see cref="MessageFactory.ParseMessages"/>, validated for community string, run through
/// the middleware pipeline, looked up in the device registry, OID-filtered, and routed to
/// the appropriate device channel.
/// <para>
/// Poll responses bypass this service entirely -- they go directly from poll jobs to the
/// extractor via <see cref="ISnmpExtractor"/> (PIPE-06).
/// </para>
/// </summary>
public sealed class SnmpListenerService : BackgroundService
{
    private readonly SnmpListenerOptions _listenerOptions;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ITrapFilter _trapFilter;
    private readonly IDeviceChannelManager _channelManager;
    private readonly TrapMiddlewareDelegate _pipeline;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly ILogger<SnmpListenerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpListenerService"/> class.
    /// </summary>
    /// <param name="listenerOptions">SNMP listener bind address, port, and community string.</param>
    /// <param name="deviceRegistry">Device registry for sender IP lookup.</param>
    /// <param name="trapFilter">OID filter for matching varbinds against device trap definitions.</param>
    /// <param name="channelManager">Channel manager for routing accepted traps to device channels.</param>
    /// <param name="pipeline">Pre-built middleware pipeline delegate (error handling, correlationId, logging).</param>
    /// <param name="pipelineMetrics">Pipeline metric service for recording trap received counters.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SnmpListenerService(
        IOptions<SnmpListenerOptions> listenerOptions,
        IDeviceRegistry deviceRegistry,
        ITrapFilter trapFilter,
        IDeviceChannelManager channelManager,
        TrapMiddlewareDelegate pipeline,
        PipelineMetricService pipelineMetrics,
        ILogger<SnmpListenerService> logger)
    {
        _listenerOptions = listenerOptions.Value;
        _deviceRegistry = deviceRegistry;
        _trapFilter = trapFilter;
        _channelManager = channelManager;
        _pipeline = pipeline;
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;
    }

    /// <summary>
    /// Binds a UdpClient to the configured endpoint and enters the receive loop.
    /// Each datagram is processed via <see cref="ProcessDatagramAsync"/>.
    /// Socket errors and unexpected exceptions are caught and logged without killing the loop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(
            IPAddress.Parse(_listenerOptions.BindAddress),
            _listenerOptions.Port);

        using var udpClient = new UdpClient(endpoint);
        var userRegistry = new UserRegistry();

        _logger.LogInformation("SNMP listener started on {Endpoint}", endpoint);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync(stoppingToken);
                await ProcessDatagramAsync(result, userRegistry, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning("Socket error receiving SNMP message: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error in SNMP listener: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("SNMP listener stopped");
    }

    /// <summary>
    /// Processes a single UDP datagram: parses SNMP messages, validates community string,
    /// runs the middleware pipeline, performs device lookup and OID filtering, and writes
    /// accepted traps to device channels.
    /// </summary>
    private async Task ProcessDatagramAsync(
        UdpReceiveResult result,
        UserRegistry userRegistry,
        CancellationToken ct)
    {
        var messages = MessageFactory.ParseMessages(
            result.Buffer, 0, result.Buffer.Length, userRegistry);

        foreach (var message in messages)
        {
            if (message is not TrapV2Message trapV2)
            {
                _logger.LogDebug(
                    "Ignoring non-trap SNMP message type {TypeCode}",
                    message.TypeCode());
                continue;
            }

            if (trapV2.Community().ToString() != _listenerOptions.CommunityString)
            {
                _logger.LogDebug(
                    "Rejected trap with invalid community string from {SenderIp}",
                    result.RemoteEndPoint.Address);
                continue;
            }

            var senderIp = result.RemoteEndPoint.Address.MapToIPv4();

            var envelope = new TrapEnvelope
            {
                Varbinds = trapV2.Variables(),
                SenderAddress = senderIp,
                ReceivedAt = DateTimeOffset.UtcNow
            };

            var context = new TrapContext { Envelope = envelope };

            // Run middleware pipeline: error handling -> correlationId -> logging
            await _pipeline(context);

            if (context.IsRejected)
            {
                continue;
            }

            // Device lookup by normalized IPv4 address
            if (!_deviceRegistry.TryGetDevice(senderIp, out var device))
            {
                _logger.LogDebug(
                    "Trap from unknown device {SenderIp}, rejecting",
                    senderIp);
                continue;
            }

            // OID filtering against device trap definitions
            var matchedDef = _trapFilter.Match(envelope.Varbinds, device);
            if (matchedDef is null)
            {
                _logger.LogDebug(
                    "No matching trap definition for device {DeviceName} from {SenderIp}, rejecting",
                    device.Name,
                    senderIp);
                continue;
            }

            // Stamp matched definition and device on context
            envelope.MatchedDefinition = matchedDef;
            context.Device = device;

            // Write accepted trap to device-specific bounded channel
            await _channelManager.GetWriter(device.Name).WriteAsync(envelope, ct);

            // Record pipeline metric: trap accepted and written to channel
            var metricTags = _pipelineMetrics.BuildBaseLabels(device.Name, device.IpAddress, device.DeviceType);
            _pipelineMetrics.RecordTrapReceived(metricTags);
        }
    }
}
