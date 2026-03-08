using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Pipeline;

namespace Simetra.Telemetry;

/// <summary>
/// Options for <see cref="SimetraConsoleFormatter"/>. Extends <see cref="ConsoleFormatterOptions"/>
/// with an <see cref="IServiceProvider"/> reference used to lazily resolve DI services at write time.
/// </summary>
public sealed class SimetraConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>
    /// The service provider used to resolve <see cref="ICorrelationService"/>,
    /// <see cref="ILeaderElection"/>, and <see cref="IOptions{SiteOptions}"/> on first write.
    /// Populated by <see cref="PostConfigureSimetraFormatterOptions"/>.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; set; }
}

/// <summary>
/// Custom plain-text console formatter that prefixes every log line with site, role, and
/// correlationId context. Produces output in the format:
/// <code>
/// {timestamp} [{level}] [{site}|{role}|{correlationId}] {category} {message}
/// </code>
/// Replaces JSON structured console output with human-readable plain text suitable for
/// <c>kubectl logs</c> and local development while retaining operational context.
/// </summary>
public sealed class SimetraConsoleFormatter : ConsoleFormatter
{
    /// <summary>
    /// The formatter name used for registration and selection.
    /// </summary>
    public const string FormatterName = "simetra";

    private readonly IOptionsMonitor<SimetraConsoleFormatterOptions> _optionsMonitor;

    // Lazily resolved DI services (resolved on first Write call)
    private ICorrelationService? _correlationService;
    private ILeaderElection? _leaderElection;
    private IOptions<SiteOptions>? _siteOptions;
    private bool _servicesResolved;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimetraConsoleFormatter"/> class.
    /// </summary>
    /// <param name="optionsMonitor">Monitor for formatter options containing the service provider.</param>
    public SimetraConsoleFormatter(IOptionsMonitor<SimetraConsoleFormatterOptions> optionsMonitor)
        : base(FormatterName)
    {
        _optionsMonitor = optionsMonitor
            ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    /// <inheritdoc />
    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null)
            return;

        EnsureServicesResolved();

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var level = GetLevelAbbreviation(logEntry.LogLevel);
        var site = _siteOptions?.Value.Name ?? "unknown";
        var role = _leaderElection?.CurrentRole ?? "unknown";
        var correlationId = _correlationService?.OperationCorrelationId
            ?? _correlationService?.CurrentCorrelationId
            ?? "none";
        var category = logEntry.Category;

        textWriter.Write(timestamp);
        textWriter.Write(" [");
        textWriter.Write(level);
        textWriter.Write("] [");
        textWriter.Write(site);
        textWriter.Write('|');
        textWriter.Write(role);
        textWriter.Write('|');
        textWriter.Write(correlationId);
        textWriter.Write("] ");
        textWriter.Write(category);
        textWriter.Write(' ');
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception.ToString());
        }
    }

    /// <summary>
    /// Lazily resolves DI services from the service provider captured in options.
    /// Thread-safe: worst case resolves twice on concurrent first calls, both get same singletons.
    /// </summary>
    private void EnsureServicesResolved()
    {
        if (_servicesResolved)
            return;

        var sp = _optionsMonitor.CurrentValue.ServiceProvider;
        if (sp is null)
            return;

        _correlationService = sp.GetService<ICorrelationService>();
        _leaderElection = sp.GetService<ILeaderElection>();
        _siteOptions = sp.GetService<IOptions<SiteOptions>>();
        _servicesResolved = true;
    }

    /// <summary>
    /// Maps <see cref="LogLevel"/> to a 3-character abbreviation.
    /// </summary>
    private static string GetLevelAbbreviation(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };
}

/// <summary>
/// Post-configures <see cref="SimetraConsoleFormatterOptions"/> to inject the root
/// <see cref="IServiceProvider"/>. This allows the formatter to lazily resolve
/// DI services (correlation, leader election, site) without constructor injection,
/// which is not supported by the <see cref="ConsoleFormatter"/> infrastructure.
/// </summary>
internal sealed class PostConfigureSimetraFormatterOptions
    : IPostConfigureOptions<SimetraConsoleFormatterOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public PostConfigureSimetraFormatterOptions(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public void PostConfigure(string? name, SimetraConsoleFormatterOptions options)
        => options.ServiceProvider = _serviceProvider;
}
