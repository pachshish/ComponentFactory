using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Configuration;

public sealed class FactoryOptionsValidator : IValidateOptions<FactoryOptions>
{
    public ValidateOptionsResult Validate(string? name, FactoryOptions options)
    {
        var errors = new List<string>();

        ValidateGitLabUrl(options, errors);
        ValidateTemplateName(options, errors);
        ValidateRequiredValues(options, errors);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateGitLabUrl(FactoryOptions options, List<string> errors)
    {
        var isValid = Uri.TryCreate(options.GitLabUrl, UriKind.Absolute, out var url)
            && url.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(url.UserInfo)
            && string.IsNullOrEmpty(url.Query)
            && string.IsNullOrEmpty(url.Fragment);

        if (!isValid)
        {
            errors.Add("Factory:GitLabUrl must be an HTTPS URL without credentials, query or fragment.");
        }
    }

    private static void ValidateTemplateName(FactoryOptions options, List<string> errors)
    {
        var templateName = options.TemplateName;
        var isValid = !string.IsNullOrEmpty(templateName)
            && Regex.IsMatch(templateName, "\\A[A-Z][A-Za-z0-9]*\\z")
            && templateName != templateName.ToUpperInvariant();

        if (!isValid)
        {
            errors.Add("Factory:TemplateName must be PascalCase with distinct lower/upper variants.");
        }
    }

    private static void ValidateRequiredValues(FactoryOptions options, List<string> errors)
    {
        if (options.NamespaceId <= 0 || options.TemplateProjectId <= 0)
        {
            errors.Add("Factory:NamespaceId and TemplateProjectId must be positive IDs.");
        }

        if (string.IsNullOrWhiteSpace(options.Token) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            errors.Add("Factory:Token and ApiKey are required.");
        }

        if (options.OperationTimeoutSeconds <= 0)
        {
            errors.Add("Factory:OperationTimeoutSeconds must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkRoot) || !Path.IsPathFullyQualified(options.WorkRoot))
        {
            errors.Add("Factory:WorkRoot must be an absolute path.");
        }
    }
}
