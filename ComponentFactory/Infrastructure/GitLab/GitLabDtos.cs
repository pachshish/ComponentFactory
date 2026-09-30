using System.Text.Json.Serialization;
using ComponentFactory.Application.Models;

namespace ComponentFactory.Infrastructure.GitLab;

internal sealed record GitLabProjectResponse(
    long Id,
    [property: JsonPropertyName("web_url")] string WebUrl,
    [property: JsonPropertyName("http_url_to_repo")] string RepositoryUrl)
{
    public GitLabProject ToProject()
    {
        return new GitLabProject(Id, WebUrl, RepositoryUrl);
    }
}

internal sealed record GitLabGroupResponse(
    [property: JsonPropertyName("full_path")] string FullPath);

internal sealed record CreateProjectPayload(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("namespace_id")] long NamespaceId,
    [property: JsonPropertyName("visibility")] string Visibility = "private",
    [property: JsonPropertyName("initialize_with_readme")] bool InitializeWithReadme = false);
