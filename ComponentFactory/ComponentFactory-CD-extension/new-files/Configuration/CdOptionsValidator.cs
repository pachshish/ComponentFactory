using ComponentFactory.Domain;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Configuration;

public sealed class CdOptionsValidator : IValidateOptions<CdOptions>
{
    public ValidateOptionsResult Validate(string? name, CdOptions options)
    {
        var errors = new List<string>();

        if (options.ProjectId <= 0)
        {
            errors.Add("Cd:ProjectId must identify the existing CD project (a positive ID).");
        }

        if (string.IsNullOrWhiteSpace(options.Branch) || options.Branch.StartsWith('-'))
        {
            errors.Add("Cd:Branch must be a nonempty Git branch name, without a leading '-'.");
        }

        ValidateRelativePath(options.TemplatePath, "Cd:TemplatePath", errors);
        ValidateRelativePath(options.TargetRoot, "Cd:TargetRoot", errors);

        try
        {
            var templateName = ComponentName.Create(options.TemplateName);
            if (templateName.Value == templateName.UpperCase)
            {
                errors.Add("Cd:TemplateName must have distinct PascalCase/lowercase/uppercase variants.");
            }
        }
        catch (ComponentFactoryException)
        {
            errors.Add("Cd:TemplateName must follow the existing component name convention. Example: SenorTemplate.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateRelativePath(string path, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':'))
        {
            errors.Add($"{key} must be a repository-relative path using '/' separators.");
            return;
        }

        foreach (var segment in path.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(segment) || segment.StartsWith('.') ||
                segment.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            {
                errors.Add($"{key} must contain ordinary directory names without traversal, Git metadata or wildcard characters.");
                return;
            }
        }
    }
}
