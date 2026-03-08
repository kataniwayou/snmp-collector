using Microsoft.Extensions.Logging;
using Simetra.Configuration;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Orchestrates extraction result processing through two independent branches:
/// Branch A (metrics via <see cref="IMetricFactory"/>) and Branch B (State Vector via
/// <see cref="IStateVectorService"/>). Source-based routing (PROC-06) gates Branch B to
/// <see cref="MetricPollSource.Module"/> only. Independent try/catch blocks (PROC-08) ensure
/// a failure in one branch does not prevent the other from executing.
/// </summary>
public sealed class ProcessingCoordinator : IProcessingCoordinator
{
    private readonly IMetricFactory _metricFactory;
    private readonly IStateVectorService _stateVector;
    private readonly ILogger<ProcessingCoordinator> _logger;

    public ProcessingCoordinator(
        IMetricFactory metricFactory,
        IStateVectorService stateVector,
        ILogger<ProcessingCoordinator> logger)
    {
        _metricFactory = metricFactory;
        _stateVector = stateVector;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Process(ExtractionResult result, DeviceInfo device, string correlationId)
    {
        // Branch A: Metrics -- ALWAYS runs (both Module and Configuration sources)
        try
        {
            _metricFactory.RecordMetrics(result, device);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Branch A (metrics) failed for {MetricName} on {DeviceName}",
                result.Definition.MetricName,
                device.Name);
        }

        // Branch B: State Vector -- ONLY for Source=Module (PROC-06)
        if (result.Definition.Source == MetricPollSource.Module)
        {
            try
            {
                _stateVector.Update(device.Name, result.Definition.MetricName, result, correlationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Branch B (state vector) failed for {MetricName} on {DeviceName}",
                    result.Definition.MetricName,
                    device.Name);
            }
        }
    }
}
