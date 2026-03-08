using System.Globalization;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Simetra.Configuration;
using Simetra.Models;
using Simetra.Pipeline;

namespace Simetra.Services;

/// <summary>
/// Generic SNMP varbind extractor. Pattern-matches each varbind's ISnmpData type
/// and produces an <see cref="ExtractionResult"/> with metrics, labels, and enum-map metadata.
/// Same logic for traps and polls -- no per-device-type branching.
/// </summary>
public sealed class SnmpExtractorService : ISnmpExtractor
{
    private readonly ILogger<SnmpExtractorService> _logger;

    public SnmpExtractorService(ILogger<SnmpExtractorService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ExtractionResult Extract(IList<Variable> varbinds, PollDefinitionDto definition)
    {
        var oidLookup = definition.Oids.ToDictionary(o => o.Oid, o => o);

        var metrics = new Dictionary<string, double>();
        var labels = new Dictionary<string, string>();
        var enumMetadata = new Dictionary<string, IReadOnlyDictionary<int, string>>();

        foreach (var varbind in varbinds)
        {
            var oidString = varbind.Id.ToString();

            if (!oidLookup.TryGetValue(oidString, out var entry))
            {
                _logger.LogDebug(
                    "Varbind OID {Oid} not found in definition {MetricName}, skipping",
                    oidString, definition.MetricName);
                continue;
            }

            switch (entry.Role)
            {
                case OidRole.Metric:
                    var numericValue = ExtractNumericValue(varbind.Data);
                    if (numericValue.HasValue)
                    {
                        metrics[entry.PropertyName] = numericValue.Value;

                        if (entry.EnumMap is { Count: > 0 })
                        {
                            enumMetadata[entry.PropertyName] = entry.EnumMap;
                        }
                    }
                    else
                    {
                        if (varbind.Data is OctetString)
                        {
                            _logger.LogDebug(
                                "Unparseable OctetString {Value} for Metric role OID {Oid}, skipping",
                                varbind.Data.ToString(), oidString);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Non-numeric SNMP data {TypeCode} for Metric role OID {Oid}, skipping",
                                varbind.Data.TypeCode, oidString);
                        }
                    }
                    break;

                case OidRole.Label:
                    var labelValue = ExtractLabelValue(varbind.Data, entry.EnumMap);
                    if (labelValue is not null)
                    {
                        labels[entry.PropertyName] = labelValue;
                    }
                    break;
            }
        }

        return new ExtractionResult
        {
            Definition = definition,
            Metrics = metrics,
            Labels = labels,
            EnumMapMetadata = enumMetadata
        };
    }

    private static double? ExtractNumericValue(ISnmpData data)
    {
        return data switch
        {
            Integer32 i => i.ToInt32(),
            Counter32 c32 => c32.ToUInt32(),
            Counter64 c64 => c64.ToUInt64(),
            Gauge32 g => g.ToUInt32(),
            TimeTicks tt => tt.ToUInt32(),
            OctetString os when double.TryParse(
                os.ToString(),
                NumberStyles.Float | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ExtractLabelValue(ISnmpData data, IReadOnlyDictionary<int, string>? enumMap)
    {
        if (enumMap is not null && data is Integer32 i32)
        {
            return enumMap.TryGetValue(i32.ToInt32(), out var mapped)
                ? mapped
                : i32.ToInt32().ToString();
        }

        return data switch
        {
            OctetString os => os.ToString(),
            IP ip => ip.ToString(),
            Integer32 i => i.ToInt32().ToString(),
            _ => data.ToString()
        };
    }
}
