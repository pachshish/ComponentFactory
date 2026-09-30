using ComponentFactory.Application.Models;
using ComponentFactory.Domain;

namespace ComponentFactory.Application.Abstractions;

public interface IGitRepository
{
    Task CloneTemplateAsync(
        GitLabProject template,
        IWorkspace workspace,
        CancellationToken cancellationToken);

    void RemoveTemplateHistory(IWorkspace workspace);

    Task CreateInitialCommitAsync(
        IWorkspace workspace,
        ComponentName name,
        CancellationToken cancellationToken);

    Task PushAsync(
        IWorkspace workspace,
        GitLabProject target,
        CancellationToken cancellationToken);
}
