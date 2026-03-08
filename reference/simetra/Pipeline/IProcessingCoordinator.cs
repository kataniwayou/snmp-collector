using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Processes an <see cref="ExtractionResult"/> through the dual-branch pipeline.
/// Branch A (metrics) always executes for all sources. Branch B (State Vector) executes
/// only when <see cref="PollDefinitionDto.Source"/> is <see cref="Configuration.MetricPollSource.Module"/>
/// (PROC-06 source-based routing). Each branch runs independently -- a failure in one does not
/// block execution of the other (PROC-08 branch isolation).
/// </summary>
public interface IProcessingCoordinator
{
    /// <summary>
    /// Routes the extraction result through Branch A (metric recording) and optionally
    /// Branch B (State Vector update) based on the result's source routing rules.
    /// </summary>
    /// <param name="result">Extraction result containing metrics and labels.</param>
    /// <param name="device">Device identity providing base label values.</param>
    /// <param name="correlationId">Correlation ID of the originating poll or trap.</param>
    void Process(ExtractionResult result, DeviceInfo device, string correlationId);
}
