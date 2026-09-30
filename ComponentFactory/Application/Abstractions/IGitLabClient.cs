using ComponentFactory.Application.Models;

namespace ComponentFactory.Application.Abstractions;

public interface IGitLabClient
{
    Task EnsureProjectNameAvailableAsync(
        long namespaceId,
        string projectPath,
        CancellationToken cancellationToken);

    Task<GitLabProject> GetProjectAsync(
        long projectId,
        CancellationToken cancellationToken);

    Task<GitLabProject> CreateProjectAsync(
        long namespaceId,
        string projectPath,
        CancellationToken cancellationToken);
}
