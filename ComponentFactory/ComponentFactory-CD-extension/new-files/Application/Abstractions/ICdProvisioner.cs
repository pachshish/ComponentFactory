using ComponentFactory.Domain;

namespace ComponentFactory.Application.Abstractions;

public interface ICdProvisioner
{
    Task ProvisionAsync(ComponentName name, CancellationToken cancellationToken);
}
