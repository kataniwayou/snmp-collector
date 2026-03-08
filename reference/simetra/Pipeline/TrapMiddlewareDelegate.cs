namespace Simetra.Pipeline;

/// <summary>
/// Delegate representing a single step in the trap processing pipeline.
/// Analogous to ASP.NET Core's <c>RequestDelegate</c>, each middleware invokes the
/// next delegate to pass control down the chain.
/// </summary>
public delegate Task TrapMiddlewareDelegate(TrapContext context);
