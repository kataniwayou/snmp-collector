using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Simetra.Configuration;
using Simetra.Pipeline;
using Simetra.Telemetry;

namespace Simetra.Jobs;

/// <summary>
/// Polls a device for state data (Source=Module) using SNMP GET, extracts results via
/// the generic extractor, and feeds them to the processing pipeline.
/// Bypasses Layer 2 channels and feeds directly to Layer 3/4 (PIPE-06).
/// </summary>
[DisallowConcurrentExecution]
public sealed class StatePollJob : IJob
{
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IPollDefinitionRegistry _pollRegistry;
    private readonly ISnmpExtractor _extractor;
    private readonly IProcessingCoordinator _coordinator;
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly SnmpListenerOptions _listenerOptions;
    private readonly ILogger<StatePollJob> _logger;

    public StatePollJob(
        IDeviceRegistry deviceRegistry,
        IPollDefinitionRegistry pollRegistry,
        ISnmpExtractor extractor,
        IProcessingCoordinator coordinator,
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        PipelineMetricService pipelineMetrics,
        IOptions<SnmpListenerOptions> listenerOptions,
        ILogger<StatePollJob> logger)
    {
        _deviceRegistry = deviceRegistry;
        _pollRegistry = pollRegistry;
        _extractor = extractor;
        _coordinator = coordinator;
        _correlation = correlation;
        _liveness = liveness;
        _pipelineMetrics = pipelineMetrics;
        _listenerOptions = listenerOptions.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        // SCHED-08: Read correlationId BEFORE execution and scope it for the formatter
        var correlationId = _correlation.CurrentCorrelationId;
        _correlation.OperationCorrelationId = correlationId;
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            var deviceName = context.MergedJobDataMap.GetString("deviceName")!;
            var pollKey = context.MergedJobDataMap.GetString("pollKey")!;

            if (!_deviceRegistry.TryGetDeviceByName(deviceName, out var device))
            {
                _logger.LogWarning(
                    "State poll job {JobKey}: device {DeviceName} not found in registry",
                    jobKey, deviceName);
                return;
            }

            if (!_pollRegistry.TryGetDefinition(deviceName, pollKey, out var definition))
            {
                _logger.LogWarning(
                    "State poll job {JobKey}: definition {PollKey} not found for device {DeviceName}",
                    jobKey, pollKey, deviceName);
                return;
            }

            var variables = definition.Oids
                .Select(o => new Variable(new ObjectIdentifier(o.Oid)))
                .ToList();

            var endpoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), 161);
            var community = new OctetString(_listenerOptions.CommunityString);

            var intervalSeconds = context.MergedJobDataMap.GetInt("intervalSeconds");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(intervalSeconds * 0.8));

            IList<Variable> response = await Messenger.GetAsync(
                VersionCode.V2,
                endpoint,
                community,
                variables,
                timeoutCts.Token);

            // Extract + Process (Layer 3/4 -- bypass Layer 2 channels per PIPE-06)
            var result = _extractor.Extract(response, definition);
            _coordinator.Process(result, device, correlationId);

            // Record pipeline metric: poll executed successfully
            var metricTags = _pipelineMetrics.BuildBaseLabels(device.Name, device.IpAddress, device.DeviceType);
            _pipelineMetrics.RecordPollExecuted(metricTags);

            _logger.LogDebug(
                "State poll completed for {DeviceName}/{PollKey}",
                deviceName, pollKey);
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            // SNMP timeout (not shutdown) -- log warning and let finally stamp liveness
            _logger.LogWarning(
                "State poll job {JobKey} timed out waiting for SNMP response",
                jobKey);
        }
        catch (OperationCanceledException)
        {
            // Shutdown signal -- let it propagate
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "State poll job {JobKey} failed",
                jobKey);
        }
        finally
        {
            _correlation.OperationCorrelationId = null;
            // SCHED-08: Stamp liveness vector on completion (always, even on failure)
            _liveness.Stamp(jobKey);
        }
    }
}
