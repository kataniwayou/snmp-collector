using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Models;
using Simetra.Telemetry;

namespace Simetra.Pipeline;

/// <summary>
/// Creates and records OTLP-ready metrics using <see cref="System.Diagnostics.Metrics"/>.
/// Instruments are cached by name to avoid re-creation (a single Gauge or Counter per unique
/// metric name). Base labels (site_name, device_name, device_ip, device_type) are enforced on every
/// measurement, with optional StaticLabels from the poll definition injected next, followed by
/// dynamic labels from Role:Label OID values. Extracted (dynamic) labels win over StaticLabels
/// on key collision.
/// </summary>
public sealed class MetricFactory : IMetricFactory
{
    private readonly Meter _meter;
    private readonly SiteOptions _siteOptions;
    private readonly ILogger<MetricFactory> _logger;
    private readonly ConcurrentDictionary<string, object> _instruments = new();

    public MetricFactory(
        IMeterFactory meterFactory,
        IOptions<SiteOptions> siteOptions,
        ILogger<MetricFactory> logger)
    {
        _meter = meterFactory.Create(TelemetryConstants.LeaderMeterName);
        _siteOptions = siteOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public void RecordMetrics(ExtractionResult result, DeviceInfo device)
    {
        foreach (var (propertyName, value) in result.Metrics)
        {
            try
            {
                var metricName = propertyName;

                var tags = new TagList
                {
                    { "site_name", _siteOptions.Name },
                    { "device_name", device.Name },
                    { "device_ip", device.IpAddress },
                    { "device_type", device.DeviceType }
                };

                // StaticLabels: after base labels, before dynamic labels
                if (result.Definition.StaticLabels is { Count: > 0 })
                {
                    foreach (var (key, val) in result.Definition.StaticLabels)
                    {
                        // Extracted (dynamic) label wins on key collision
                        if (!result.Labels.ContainsKey(key))
                        {
                            tags.Add(key, val);
                        }
                    }
                }

                // Dynamic labels from extraction (OidRole.Label varbinds)
                foreach (var (labelName, labelValue) in result.Labels)
                {
                    tags.Add(labelName, labelValue);
                }

                var instrument = GetOrCreateInstrument(metricName, result.Definition.MetricType);
                RecordValue(instrument, value, tags);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to record metric {PropertyName} for device {DeviceName}",
                    propertyName,
                    device.Name);
            }
        }
    }

    private object GetOrCreateInstrument(string name, MetricType type)
    {
        return _instruments.GetOrAdd(name, n => type switch
        {
            MetricType.Gauge => _meter.CreateGauge<double>(n),
            MetricType.Counter => _meter.CreateCounter<double>(n),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported metric type")
        });
    }

    private static void RecordValue(object instrument, double value, TagList tags)
    {
        if (instrument is Gauge<double> gauge)
        {
            gauge.Record(value, tags);
        }
        else if (instrument is Counter<double> counter)
        {
            counter.Add(value, tags);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unexpected instrument type: {instrument.GetType().Name}");
        }
    }
}
