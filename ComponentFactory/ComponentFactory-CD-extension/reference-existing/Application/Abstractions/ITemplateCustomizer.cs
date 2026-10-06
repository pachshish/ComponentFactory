using ComponentFactory.Domain;

namespace ComponentFactory.Application.Abstractions;

public interface ITemplateCustomizer
{
    Task CustomizeAsync(
        string repositoryPath,
        ComponentName name,
        CancellationToken cancellationToken);

    Task CustomizeAsync(
        string rootPath,
        ComponentName name,
        string templateName,
        CancellationToken cancellationToken);
}
