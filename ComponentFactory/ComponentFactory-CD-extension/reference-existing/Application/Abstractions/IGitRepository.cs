using ComponentFactory.Application.Models;
using ComponentFactory.Domain;

namespace ComponentFactory.Application.Abstractions;

public interface IGitRepository
{
    Task CloneAsync(
        GitLabProject project,
        IWorkspace workspace,
        string branch,
        CancellationToken cancellationToken);

    Task CloneTemplateAsync(
        GitLabProject template,
        IWorkspace workspace,
        CancellationToken cancellationToken);

    Task CreateInitialCommitAsync(
        IWorkspace workspace,
        ComponentName name,
        CancellationToken cancellationToken);

    Task PushAsync(
        IWorkspace workspace,
        GitLabProject target,
        CancellationToken cancellationToken);

    Task CommitAsync(
        IWorkspace workspace,
        string message,
        string relativePath,
        CancellationToken cancellationToken);

    Task PushAsync(
        IWorkspace workspace,
        GitLabProject target,
        string branch,
        CancellationToken cancellationToken);

    Task RefreshBranchAsync(
        IWorkspace workspace,
        string branch,
        CancellationToken cancellationToken);
}
