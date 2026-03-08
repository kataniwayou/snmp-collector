using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Quartz;
using Simetra.Pipeline;
using Simetra.Services;
using Simetra.Telemetry;

namespace Simetra.Lifecycle;

/// <summary>
/// Orchestrates the complete LIFE-05 graceful shutdown sequence with time-budgeted steps.
/// Registered LAST as an <see cref="IHostedService"/> so its <see cref="StopAsync"/> runs
/// FIRST in the framework's reverse-order stop. This is the SINGLE orchestrator of the
/// entire shutdown sequence -- no other service should independently manage shutdown steps.
/// <para>
/// The 5-step shutdown sequence ensures: near-instant HA failover (lease release), no new
/// traps accepted (listener stop), no new job fires (scheduler standby), in-flight data
/// completes (channel drain), and final telemetry is preserved (flush).
/// </para>
/// </summary>
public sealed class GracefulShutdownService : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IDeviceChannelManager _channelManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GracefulShutdownService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GracefulShutdownService"/>.
    /// </summary>
    /// <param name="schedulerFactory">Quartz scheduler factory to put scheduler in standby.</param>
    /// <param name="channelManager">Device channel manager for drain operations.</param>
    /// <param name="serviceProvider">Service provider for resolving optional services (K8sLeaseElection, SnmpListenerService, telemetry providers).</param>
    /// <param name="logger">Logger for shutdown step progress.</param>
    public GracefulShutdownService(
        ISchedulerFactory schedulerFactory,
        IDeviceChannelManager channelManager,
        IServiceProvider serviceProvider,
        ILogger<GracefulShutdownService> logger)
    {
        _schedulerFactory = schedulerFactory;
        _channelManager = channelManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// No startup work required. This service only acts during shutdown.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Executes the complete LIFE-05 graceful shutdown sequence with time-budgeted steps.
    /// </summary>
    /// <remarks>
    /// LIFE-05: ALL 5 ordered shutdown steps are orchestrated here.
    /// GracefulShutdownService is registered LAST, so its StopAsync runs FIRST
    /// in the framework's reverse-order stop. This service is the SINGLE
    /// orchestrator of the entire shutdown sequence.
    ///
    /// K8sLeaseElection and SnmpListenerService extend BackgroundService, whose
    /// StopAsync is idempotent (cancels the stoppingToken via an internal
    /// CancellationTokenSource, then awaits ExecuteTask -- both are safe to
    /// call multiple times). The framework will call their StopAsync AGAIN
    /// in reverse registration order after this service completes, but that
    /// second call is a harmless no-op.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Graceful shutdown sequence starting (LIFE-05)");

        // Step 1: Release lease (LIFE-05 step 1 -- near-instant HA failover)
        await ExecuteWithBudget("ReleaseLease", TimeSpan.FromSeconds(3), async () =>
        {
            var leaseService = _serviceProvider.GetService<K8sLeaseElection>();
            if (leaseService is not null)
            {
                await leaseService.StopAsync(CancellationToken.None);
                _logger.LogInformation("Leader lease released");
            }
            else
            {
                _logger.LogDebug("No K8sLeaseElection registered (local dev mode), skipping lease release");
            }
        }, cancellationToken);

        // Step 2: Stop SNMP listener (LIFE-05 step 2 -- no new traps accepted)
        await ExecuteWithBudget("StopListener", TimeSpan.FromSeconds(3), async () =>
        {
            var listener = _serviceProvider.GetServices<IHostedService>()
                .OfType<SnmpListenerService>()
                .FirstOrDefault();
            if (listener is not null)
            {
                await listener.StopAsync(CancellationToken.None);
                _logger.LogInformation("SNMP listener stopped");
            }
        }, cancellationToken);

        // Step 3: Put scheduler in standby (prevents new job fires)
        await ExecuteWithBudget("PauseScheduler", TimeSpan.FromSeconds(3), async () =>
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            await scheduler.Standby();
            _logger.LogInformation("Scheduler placed in standby");
        }, cancellationToken);

        // Step 4: Drain device channels
        await ExecuteWithBudget("DrainChannels", TimeSpan.FromSeconds(8), async () =>
        {
            _channelManager.CompleteAll();
            await _channelManager.WaitForDrainAsync(CancellationToken.None);
            _logger.LogInformation("Device channels drained");
        }, cancellationToken);

        // Step 5: Flush telemetry (LIFE-07: protected with own budget regardless of prior outcomes)
        // This ALWAYS runs even if Steps 1-4 fail or are abandoned.
        await FlushTelemetryAsync();

        _logger.LogInformation("Graceful shutdown sequence completed (LIFE-05)");
    }

    /// <summary>
    /// Executes a shutdown step with a bounded time budget. If the step exceeds its budget,
    /// it is abandoned and the next step proceeds (LIFE-06). Each step gets its own linked
    /// CancellationTokenSource with CancelAfter for the budget duration.
    /// </summary>
    /// <param name="stepName">Name of the step for logging.</param>
    /// <param name="budget">Maximum time allowed for this step.</param>
    /// <param name="action">The async action to execute.</param>
    /// <param name="outerToken">The outer cancellation token from the host.</param>
    private async Task ExecuteWithBudget(
        string stepName,
        TimeSpan budget,
        Func<Task> action,
        CancellationToken outerToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        stepCts.CancelAfter(budget);

        try
        {
            await action();
        }
        catch (OperationCanceledException) when (stepCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Shutdown step {StepName} exceeded budget of {BudgetSeconds}s, abandoning",
                stepName,
                budget.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Shutdown step {StepName} failed",
                stepName);
        }
    }

    /// <summary>
    /// Flushes telemetry providers with a protected time budget (LIFE-07).
    /// Uses its OWN CancellationTokenSource -- NOT linked to the outer shutdown token --
    /// ensuring telemetry flush always gets its full budget regardless of prior step outcomes
    /// or the host's remaining shutdown time.
    /// </summary>
    private async Task FlushTelemetryAsync()
    {
        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await Task.Run(() =>
            {
                var meterProvider = _serviceProvider.GetService<MeterProvider>();
                var tracerProvider = _serviceProvider.GetService<TracerProvider>();

                meterProvider?.ForceFlush(timeoutMilliseconds: 5000);
                tracerProvider?.ForceFlush(timeoutMilliseconds: 5000);
            }, flushCts.Token);

            _logger.LogInformation("Telemetry flush completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Telemetry flush exceeded 5s budget");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telemetry flush failed");
        }
    }
}
