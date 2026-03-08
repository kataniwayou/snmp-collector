namespace Simetra.Pipeline.Middleware;

/// <summary>
/// Stamps the current correlation ID from <see cref="ICorrelationService"/> onto the
/// <see cref="TrapEnvelope"/> before forwarding to inner middleware. This ensures the
/// correlation ID is available for downstream logging and Layer 2 processing (PIPE-08).
/// </summary>
public sealed class CorrelationIdMiddleware : ITrapMiddleware
{
    private readonly ICorrelationService _correlationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationIdMiddleware"/> class.
    /// </summary>
    /// <param name="correlationService">Service providing the current correlation ID.</param>
    public CorrelationIdMiddleware(ICorrelationService correlationService)
    {
        _correlationService = correlationService;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(TrapContext context, TrapMiddlewareDelegate next)
    {
        context.Envelope.CorrelationId = _correlationService.CurrentCorrelationId;
        _correlationService.OperationCorrelationId = context.Envelope.CorrelationId;
        try
        {
            await next(context);
        }
        finally
        {
            _correlationService.OperationCorrelationId = null;
        }
    }
}
