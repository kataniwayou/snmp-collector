using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Singleton service that owns all 6 pipeline counter instruments on the SnmpCollector meter.
/// Creating counters here (once) avoids duplicate instrument registration and provides a single
/// injection point for all pipeline behaviors and handlers that need to record metrics.
/// </summary>
public sealed class PipelineMetricService : IDisposable
{
    private readonly Meter _meter;
    private readonly string _siteName;

    // PMET-01: counts every SnmpOidReceived notification published into the MediatR pipeline
    private readonly Counter<long> _published;

    // PMET-02: counts every notification that reached a terminal handler without error
    private readonly Counter<long> _handled;

    // PMET-03: counts pipeline errors (exceptions in behaviors or handlers)
    private readonly Counter<long> _errors;

    // PMET-04: counts notifications discarded before reaching a handler (e.g., unresolved OID)
    private readonly Counter<long> _rejected;

    // PMET-05: counts scheduled poll executions
    private readonly Counter<long> _pollExecuted;

    // PMET-06: counts inbound trap messages received by the listener
    private readonly Counter<long> _trapReceived;

    public PipelineMetricService(IMeterFactory meterFactory, IOptions<SiteOptions> siteOptions)
    {
        _meter = meterFactory.Create(TelemetryConstants.MeterName);
        _siteName = siteOptions.Value.Name;

        _published = _meter.CreateCounter<long>("snmp.event.published");
        _handled = _meter.CreateCounter<long>("snmp.event.handled");
        _errors = _meter.CreateCounter<long>("snmp.event.errors");
        _rejected = _meter.CreateCounter<long>("snmp.event.rejected");
        _pollExecuted = _meter.CreateCounter<long>("snmp.poll.executed");
        _trapReceived = _meter.CreateCounter<long>("snmp.trap.received");
    }

    /// <summary>PMET-01: Increment the count of published pipeline notifications by 1.</summary>
    public void IncrementPublished()
        => _published.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-02: Increment the count of successfully handled notifications by 1.</summary>
    public void IncrementHandled()
        => _handled.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-03: Increment the count of pipeline errors by 1.</summary>
    public void IncrementErrors()
        => _errors.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-04: Increment the count of rejected (discarded) notifications by 1.</summary>
    public void IncrementRejected()
        => _rejected.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-05: Increment the count of executed poll cycles by 1.</summary>
    public void IncrementPollExecuted()
        => _pollExecuted.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-06: Increment the count of received trap messages by 1.</summary>
    public void IncrementTrapReceived()
        => _trapReceived.Add(1, new TagList { { "site_name", _siteName } });

    public void Dispose() => _meter.Dispose();
}
