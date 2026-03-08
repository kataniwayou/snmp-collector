using Simetra.Pipeline;
using Simetra.Telemetry;

namespace Simetra.Services;

/// <summary>
/// BackgroundService that reads <see cref="TrapEnvelope"/> items from per-device channels
/// via <see cref="System.Threading.Channels.ChannelReader{T}.ReadAllAsync"/> and drives each
/// through the consumer-side middleware pipeline, <see cref="ISnmpExtractor"/> (Layer 3),
/// and <see cref="IProcessingCoordinator"/> (Layer 4: metrics + State Vector).
/// <para>
/// One consumer task is spawned per device channel. The loop ends naturally when the
/// channel writer is completed (by <see cref="Lifecycle.GracefulShutdownService"/> step 4)
/// or when the <c>stoppingToken</c> is cancelled.
/// </para>
/// <para>
/// Satisfies TRAP-01 (channel reader), TRAP-02 (consumer-side middleware via
/// <see cref="TrapPipelineBuilder"/>), TRAP-03 (extraction), TRAP-04 (processing
/// coordinator routing), TRAP-05 (graceful shutdown), TRAP-06 (structured logging).
/// </para>
/// </summary>
public sealed class ChannelConsumerService : BackgroundService
{
    private readonly IDeviceChannelManager _channelManager;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ISnmpExtractor _extractor;
    private readonly IProcessingCoordinator _coordinator;
    private readonly ICorrelationService _correlation;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly ILogger<ChannelConsumerService> _logger;
    private readonly TrapMiddlewareDelegate _consumerPipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelConsumerService"/> class and
    /// builds the consumer-side middleware pipeline (TRAP-02).
    /// </summary>
    /// <param name="channelManager">Provides per-device channel readers.</param>
    /// <param name="deviceRegistry">Resolves device info by name for processing.</param>
    /// <param name="extractor">Layer 3 extraction of varbinds into metrics/labels.</param>
    /// <param name="coordinator">Layer 4 dual-branch processing (metrics + State Vector).</param>
    /// <param name="correlation">Correlation service for operation-scoped correlationId.</param>
    /// <param name="pipelineMetrics">Pipeline metric service for recording heartbeat metrics.</param>
    /// <param name="logger">Structured logger for consumer diagnostics.</param>
    public ChannelConsumerService(
        IDeviceChannelManager channelManager,
        IDeviceRegistry deviceRegistry,
        ISnmpExtractor extractor,
        IProcessingCoordinator coordinator,
        ICorrelationService correlation,
        PipelineMetricService pipelineMetrics,
        ILogger<ChannelConsumerService> logger)
    {
        _channelManager = channelManager;
        _deviceRegistry = deviceRegistry;
        _extractor = extractor;
        _coordinator = coordinator;
        _correlation = correlation;
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;

        // Build consumer-side middleware pipeline (TRAP-02).
        // Error handling is outermost, logging is inner. CorrelationId is NOT
        // re-stamped -- the envelope already carries it from the listener intake.
        var builder = new TrapPipelineBuilder();
        builder.Use(next => async context =>
        {
            // Error handling middleware (outermost)
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Consumer pipeline error for {DeviceName}",
                    context.Device?.Name ?? "unknown");
                context.IsRejected = true;
                context.RejectionReason = $"Consumer error: {ex.Message}";
            }
        });
        builder.Use(next => async context =>
        {
            // Logging middleware
            logger.LogDebug(
                "Consumer processing trap for {DeviceName}, definition: {MetricName}",
                context.Device?.Name ?? "unknown",
                context.Envelope.MatchedDefinition?.MetricName ?? "unmatched");
            await next(context);
        });
        _consumerPipeline = builder.Build();
    }

    /// <summary>
    /// Spawns one consumer task per device channel and awaits them all.
    /// Each task reads via <see cref="System.Threading.Channels.ChannelReader{T}.ReadAllAsync"/>
    /// and processes traps through the consumer pipeline, extractor, and coordinator.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerTasks = _channelManager.DeviceNames
            .Select(deviceName => ConsumeDeviceChannelAsync(deviceName, stoppingToken))
            .ToArray();

        await Task.WhenAll(consumerTasks);
    }

    /// <summary>
    /// Reads all trap envelopes from the specified device channel and processes each
    /// through the consumer pipeline. The loop ends when the channel writer completes
    /// or the <paramref name="stoppingToken"/> is cancelled.
    /// </summary>
    private async Task ConsumeDeviceChannelAsync(
        string deviceName, CancellationToken stoppingToken)
    {
        var reader = _channelManager.GetReader(deviceName);

        _logger.LogInformation(
            "Channel consumer started for device {DeviceName}",
            deviceName);

        await foreach (var envelope in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessTrapEnvelopeAsync(envelope, deviceName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing trap for device {DeviceName}",
                    deviceName);
            }
        }

        _logger.LogInformation(
            "Channel consumer completed for device {DeviceName}",
            deviceName);
    }

    /// <summary>
    /// Processes a single trap envelope: validates it has a matched definition and device,
    /// runs through the consumer middleware pipeline, extracts via <see cref="ISnmpExtractor"/>,
    /// and routes through <see cref="IProcessingCoordinator"/>.
    /// </summary>
    private async Task ProcessTrapEnvelopeAsync(TrapEnvelope envelope, string deviceName)
    {
        // Scope the operation correlationId from the envelope (stamped at receipt time)
        // so the formatter/enrichment use the correct interval, not the current global
        _correlation.OperationCorrelationId = envelope.CorrelationId;
        try
        {
            if (envelope.MatchedDefinition is null)
            {
                _logger.LogDebug(
                    "Trap without matched definition for {DeviceName}, skipping",
                    deviceName);
                return;
            }

            if (!_deviceRegistry.TryGetDeviceByName(deviceName, out var device))
            {
                _logger.LogWarning(
                    "Device {DeviceName} not found in registry during consumption",
                    deviceName);
                return;
            }

            // Build TrapContext for middleware pipeline compatibility
            var trapContext = new TrapContext
            {
                Envelope = envelope,
                Device = device
            };

            // Run consumer-side middleware pipeline (TRAP-02: error handling + logging)
            await _consumerPipeline(trapContext);

            if (trapContext.IsRejected)
            {
                return;
            }

            // Layer 3: Extract varbinds into metrics/labels using matched definition
            var result = _extractor.Extract(envelope.Varbinds, envelope.MatchedDefinition);

            // Layer 4: Process (Branch A: metrics always, Branch B: State Vector if Source=Module)
            _coordinator.Process(result, device, envelope.CorrelationId);

            // Record heartbeat pipeline metrics for simetra-supervisor device
            if (string.Equals(deviceName, "simetra-supervisor", StringComparison.Ordinal))
            {
                _pipelineMetrics.RecordHeartbeatProcessed();
                var duration = (DateTimeOffset.UtcNow - envelope.ReceivedAt).TotalSeconds;
                _pipelineMetrics.RecordHeartbeatDuration(duration);
            }

            _logger.LogDebug(
                "Trap processed for {DeviceName}, definition: {MetricName}",
                deviceName,
                envelope.MatchedDefinition.MetricName);
        }
        finally
        {
            _correlation.OperationCorrelationId = null;
        }
    }
}
