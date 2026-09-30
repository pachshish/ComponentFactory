using ComponentFactory.Application.Models;
using ComponentFactory.Domain;

namespace ComponentFactory.Application.Abstractions;

public interface IComponentGenerator
{
    Task<GeneratedComponent> GenerateAsync(
        ComponentName name,
        CancellationToken cancellationToken);
}
