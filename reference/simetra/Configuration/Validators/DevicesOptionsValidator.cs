using Microsoft.Extensions.Options;

namespace Simetra.Configuration.Validators;

/// <summary>
/// Validates <see cref="DevicesOptions"/> at startup.
/// Manually walks the entire nested object graph (Devices -> MetricPolls -> Oids)
/// because ValidateDataAnnotations does not validate nested objects.
/// </summary>
public sealed class DevicesOptionsValidator : IValidateOptions<DevicesOptions>
{
    /// <summary>
    /// Known device types accepted by the system. Case-insensitive comparison.
    /// </summary>
    private static readonly HashSet<string> KnownDeviceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "router",
        "switch",
        "loadbalancer",
        "simetra",
        "npb",
        "obp"
    };

    public ValidateOptionsResult Validate(string? name, DevicesOptions options)
    {
        var failures = new List<string>();

        // Empty Devices[] is valid -- no devices to poll
        for (var i = 0; i < options.Devices.Count; i++)
        {
            var device = options.Devices[i];
            ValidateDevice(device, i, failures);
        }

        // Duplicate detection across all devices
        ValidateNoDuplicates(options.Devices, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateDevice(DeviceOptions device, int index, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(device.Name))
        {
            failures.Add($"Devices[{index}].Name is required");
        }

        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            failures.Add($"Devices[{index}].IpAddress is required");
        }

        if (string.IsNullOrWhiteSpace(device.DeviceType))
        {
            failures.Add($"Devices[{index}].DeviceType is required");
        }
        else if (!KnownDeviceTypes.Contains(device.DeviceType))
        {
            failures.Add($"Devices[{index}].DeviceType '{device.DeviceType}' is not a registered device type");
        }

        for (var j = 0; j < device.MetricPolls.Count; j++)
        {
            var poll = device.MetricPolls[j];
            ValidateMetricPoll(poll, index, j, failures);
        }
    }

    private static void ValidateNoDuplicates(List<DeviceOptions> devices, List<string> failures)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];

            if (!string.IsNullOrWhiteSpace(device.Name) && !seenNames.Add(device.Name))
            {
                failures.Add($"Devices[{i}].Name '{device.Name}' is a duplicate — device names must be unique");
            }

            if (!string.IsNullOrWhiteSpace(device.IpAddress) && !seenIps.Add(device.IpAddress))
            {
                failures.Add($"Devices[{i}].IpAddress '{device.IpAddress}' is a duplicate — device IPs must be unique");
            }
        }
    }

    private static void ValidateMetricPoll(MetricPollOptions poll, int deviceIndex, int pollIndex, List<string> failures)
    {
        var prefix = $"Devices[{deviceIndex}].MetricPolls[{pollIndex}]";

        if (string.IsNullOrWhiteSpace(poll.MetricName))
        {
            failures.Add($"{prefix}.MetricName is required");
        }

        if (poll.IntervalSeconds <= 0)
        {
            failures.Add($"{prefix}.IntervalSeconds must be greater than 0");
        }

        if (!Enum.IsDefined(poll.MetricType))
        {
            failures.Add($"{prefix}.MetricType '{poll.MetricType}' is not a valid MetricType");
        }

        if (poll.Oids.Count == 0)
        {
            failures.Add($"{prefix}.Oids must contain at least one entry");
        }

        for (var k = 0; k < poll.Oids.Count; k++)
        {
            var oid = poll.Oids[k];
            ValidateOidEntry(oid, prefix, k, failures);
        }
    }

    private static void ValidateOidEntry(OidEntryOptions oid, string pollPrefix, int oidIndex, List<string> failures)
    {
        var prefix = $"{pollPrefix}.Oids[{oidIndex}]";

        if (string.IsNullOrWhiteSpace(oid.Oid))
        {
            failures.Add($"{prefix}.Oid is required");
        }

        if (string.IsNullOrWhiteSpace(oid.PropertyName))
        {
            failures.Add($"{prefix}.PropertyName is required");
        }

        if (!Enum.IsDefined(oid.Role))
        {
            failures.Add($"{prefix}.Role '{oid.Role}' is not a valid OidRole");
        }
    }
}
