namespace ComponentFactory.Application.Abstractions;

public interface IWorkspaceFactory
{
    Task<IWorkspace> CreateAsync(CancellationToken cancellationToken);
}
