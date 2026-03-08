using Microsoft.Extensions.Options;

namespace Simetra.Configuration.Validators;

/// <summary>
/// Validates <see cref="SiteOptions"/> at startup.
/// </summary>
public sealed class SiteOptionsValidator : IValidateOptions<SiteOptions>
{
    public ValidateOptionsResult Validate(string? name, SiteOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Name))
        {
            failures.Add("Site:Name is required");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
