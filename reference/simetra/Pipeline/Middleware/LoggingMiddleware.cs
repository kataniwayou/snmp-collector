using Microsoft.Extensions.Logging;

namespace Simetra.Pipeline.Middleware;

/// <summary>
/// Structured logging middleware that records trap receipt, rejection, and successful
/// routing at Debug level. All log calls use named properties for structured output.
/// </summary>
public sealed class LoggingMiddleware : ITrapMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingMiddleware"/> class.
    /// </summary>
    /// <param name="logger">Logger for structured trap diagnostics.</param>
    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(TrapContext context, TrapMiddlewareDelegate next)
    {
        _logger.LogDebug(
            "Processing trap from {SenderAddress}, varbinds: {VarbindCount}",
            context.Envelope.SenderAddress,
            context.Envelope.Varbinds.Count);

        await next(context);

        if (context.IsRejected)
        {
            _logger.LogDebug(
                "Trap rejected: {RejectionReason}, sender: {SenderAddress}",
                context.RejectionReason,
                context.Envelope.SenderAddress);
        }
        else if (context.Device is not null)
        {
            _logger.LogDebug(
                "Trap routed to device {DeviceName}, definition: {MetricName}",
                context.Device.Name,
                context.Envelope.MatchedDefinition?.MetricName);
        }
    }
}
