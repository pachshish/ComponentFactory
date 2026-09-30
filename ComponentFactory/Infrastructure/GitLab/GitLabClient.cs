using System.Net;
using System.Net.Http.Json;
using ComponentFactory.Application.Abstractions;
using ComponentFactory.Application.Models;
using ComponentFactory.Domain;

namespace ComponentFactory.Infrastructure.GitLab;

public sealed class GitLabClient(HttpClient httpClient) : IGitLabClient
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<GitLabProject> GetProjectAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        var response = await GetRequiredAsync<GitLabProjectResponse>(
            $"projects/{projectId}",
            "Could not read the template project.",
            cancellationToken);

        return response.ToProject();
    }

    public async Task EnsureProjectNameAvailableAsync(
        long namespaceId,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var group = await GetRequiredAsync<GitLabGroupResponse>(
            $"groups/{namespaceId}",
            "Could not read the target group.",
            cancellationToken);

        var fullPath = Uri.EscapeDataString($"{group.FullPath}/{projectPath}");

        using var response = await _httpClient.GetAsync(
            $"projects/{fullPath}",
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            throw new ComponentFactoryException(
                FactoryError.ProjectAlreadyExists,
                "Project name already exists.");
        }

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            throw ExternalFailure("Could not check target project availability.");
        }
    }

    public async Task<GitLabProject> CreateProjectAsync(
        long namespaceId,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var payload = new CreateProjectPayload(projectPath, projectPath, namespaceId);

        using var response = await _httpClient.PostAsJsonAsync(
            "projects",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await HandleCreationFailureAsync(response, namespaceId, projectPath, cancellationToken);
        }

        var project = await ReadRequiredAsync<GitLabProjectResponse>(response, cancellationToken);

        return project.ToProject();
    }

    private async Task HandleCreationFailureAsync(
        HttpResponseMessage response,
        long namespaceId,
        string projectPath,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            // A concurrent request may have created the project after the initial check.
            await EnsureProjectNameAvailableAsync(namespaceId, projectPath, cancellationToken);
        }

        throw ExternalFailure("GitLab rejected project creation.");
    }

    private async Task<T> GetRequiredAsync<T>(
        string path,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ExternalFailure(errorMessage);
        }

        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

        return result ?? throw ExternalFailure("GitLab returned an empty response.");
    }

    private static ComponentFactoryException ExternalFailure(string message)
    {
        return new ComponentFactoryException(FactoryError.ExternalOperationFailed, message);
    }
}
