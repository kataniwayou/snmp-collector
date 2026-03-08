namespace Simetra.Pipeline;

/// <summary>
/// Implement this interface to add cross-cutting behavior to the trap processing pipeline.
/// Call <paramref name="next"/>(<paramref name="context"/>) to pass control to the next middleware,
/// or skip calling next to short-circuit the pipeline.
/// </summary>
public interface ITrapMiddleware
{
    /// <summary>
    /// Processes a trap context and optionally forwards to the next middleware in the pipeline.
    /// </summary>
    /// <param name="context">The trap processing context.</param>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvokeAsync(TrapContext context, TrapMiddlewareDelegate next);
}
