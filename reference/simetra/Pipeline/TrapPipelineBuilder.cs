namespace Simetra.Pipeline;

/// <summary>
/// Builds a composed <see cref="TrapMiddlewareDelegate"/> from registered middleware components.
/// Middleware is registered with <see cref="Use(Func{TrapMiddlewareDelegate, TrapMiddlewareDelegate})"/>
/// and composed into a single delegate via <see cref="Build"/>.
/// </summary>
public sealed class TrapPipelineBuilder
{
    private readonly List<Func<TrapMiddlewareDelegate, TrapMiddlewareDelegate>> _components = [];

    /// <summary>
    /// Adds a middleware factory to the pipeline. The factory receives the next delegate
    /// and returns a new delegate that wraps it.
    /// </summary>
    /// <param name="middleware">A factory that wraps the next delegate with middleware behavior.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public TrapPipelineBuilder Use(Func<TrapMiddlewareDelegate, TrapMiddlewareDelegate> middleware)
    {
        _components.Add(middleware);
        return this;
    }

    /// <summary>
    /// Convenience overload that wraps an <see cref="ITrapMiddleware"/> instance into
    /// the delegate factory pattern.
    /// </summary>
    /// <param name="middleware">The middleware instance to add.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public TrapPipelineBuilder Use(ITrapMiddleware middleware)
    {
        return Use(next => context => middleware.InvokeAsync(context, next));
    }

    /// <summary>
    /// Composes all registered middleware into a single delegate. Middleware executes in
    /// registration order (first registered = outermost). The terminal delegate is a no-op.
    /// </summary>
    /// <returns>A composed <see cref="TrapMiddlewareDelegate"/> ready for invocation.</returns>
    public TrapMiddlewareDelegate Build()
    {
        TrapMiddlewareDelegate pipeline = _ => Task.CompletedTask;

        for (int i = _components.Count - 1; i >= 0; i--)
        {
            pipeline = _components[i](pipeline);
        }

        return pipeline;
    }
}
