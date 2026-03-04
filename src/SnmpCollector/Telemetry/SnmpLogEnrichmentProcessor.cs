using OpenTelemetry;
using OpenTelemetry.Logs;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Telemetry;

/// <summary>
/// OpenTelemetry log processor that enriches every <see cref="LogRecord"/> with
/// site name, role, and the current correlation ID.
/// <para>
/// These attributes appear on all log records regardless of the logger category,
/// providing consistent structured context for OTLP-exported logs.
/// </para>
/// </summary>
public sealed class SnmpLogEnrichmentProcessor : BaseProcessor<LogRecord>
{
    private readonly ICorrelationService _correlationService;
    private readonly string _siteName;
    private readonly string _role;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpLogEnrichmentProcessor"/> class.
    /// </summary>
    /// <param name="correlationService">Service providing the current correlation ID.</param>
    /// <param name="siteName">Site name resolved from <c>SiteOptions.Name</c>.</param>
    /// <param name="role">
    /// Role string (e.g. "standalone", "leader", or "follower").
    /// Uses a static string in Phase 1 -- Phase 7 will make this dynamic via leader election.
    /// </param>
    public SnmpLogEnrichmentProcessor(
        ICorrelationService correlationService,
        string siteName,
        string role)
    {
        _correlationService = correlationService
            ?? throw new ArgumentNullException(nameof(correlationService));
        _siteName = siteName
            ?? throw new ArgumentNullException(nameof(siteName));
        _role = role
            ?? throw new ArgumentNullException(nameof(role));
    }

    /// <inheritdoc />
    public override void OnEnd(LogRecord data)
    {
        // Null-check Attributes -- it can be null when no structured log parameters
        // are provided (e.g. logger.LogInformation("plain message")).
        var attributes = data.Attributes?.ToList()
            ?? new List<KeyValuePair<string, object?>>(3);

        attributes.Add(new KeyValuePair<string, object?>("site_name", _siteName));
        attributes.Add(new KeyValuePair<string, object?>("role", _role));
        attributes.Add(new KeyValuePair<string, object?>("correlationId",
            _correlationService.OperationCorrelationId ?? _correlationService.CurrentCorrelationId));

        data.Attributes = attributes;
    }
}
