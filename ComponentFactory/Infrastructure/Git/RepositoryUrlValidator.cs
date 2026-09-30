using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Infrastructure.Git;

public sealed class RepositoryUrlValidator(IOptions<FactoryOptions> options)
{
    private readonly Uri _gitLabUrl = new Uri(options.Value.GitLabUrl);

    public void Validate(string repositoryUrl)
    {
        var isValid = Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var remote)
            && remote.Scheme == Uri.UriSchemeHttps
            && remote.Authority == _gitLabUrl.Authority
            && string.IsNullOrEmpty(remote.UserInfo);

        if (!isValid)
        {
            throw new ComponentFactoryException(
                FactoryError.ExternalOperationFailed,
                "Repository URL must use the configured GitLab HTTPS host without credentials.");
        }
    }
}
