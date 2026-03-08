using Microsoft.Extensions.Options;

namespace Simetra.Configuration.Validators;

/// <summary>
/// Validates <see cref="LeaseOptions"/> at startup.
/// Ensures lease duration outlives the renew interval.
/// </summary>
public sealed class LeaseOptionsValidator : IValidateOptions<LeaseOptions>
{
    public ValidateOptionsResult Validate(string? name, LeaseOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Name))
        {
            failures.Add("Lease:Name is required");
        }

        if (string.IsNullOrWhiteSpace(options.Namespace))
        {
            failures.Add("Lease:Namespace is required");
        }

        if (options.DurationSeconds <= options.RenewIntervalSeconds)
        {
            failures.Add("Lease:DurationSeconds must be greater than Lease:RenewIntervalSeconds");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
