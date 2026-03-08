using Microsoft.Extensions.Logging;

namespace Simetra.Pipeline.Middleware;

/// <summary>
/// Outermost middleware that catches exceptions from the inner pipeline, logs them
/// at Error level, and marks the trap as rejected. Never rethrows, ensuring individual
/// trap failures do not crash the listener loop.
/// </summary>
public sealed class ErrorHandlingMiddleware : ITrapMiddleware
{
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorHandlingMiddleware"/> class.
    /// </summary>
    /// <param name="logger">Logger for error diagnostics.</param>
    public ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(TrapContext context, TrapMiddlewareDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing trap from {SenderAddress}",
                context.Envelope.SenderAddress);

            context.IsRejected = true;
            context.RejectionReason = $"Error: {ex.Message}";
        }
    }
}
