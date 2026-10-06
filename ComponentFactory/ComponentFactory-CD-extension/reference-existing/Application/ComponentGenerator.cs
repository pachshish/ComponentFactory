using ComponentFactory.Application.Abstractions;
using ComponentFactory.Application.Models;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Application;

public sealed class ComponentGenerator(
    IGitLabClient gitLab,
    IGitRepository repository,
    ITemplateCustomizer customizer,
    IWorkspaceFactory workspaces,
    ICdProvisioner cdProvisioner,
    IOptions<FactoryOptions> options,
    ILogger<ComponentGenerator> logger) : IComponentGenerator
{
    private readonly IGitLabClient _gitLab = gitLab;
    private readonly IGitRepository _repository = repository;
    private readonly ITemplateCustomizer _customizer = customizer;
    private readonly IWorkspaceFactory _workspaces = workspaces;
    private readonly ICdProvisioner _cdProvisioner = cdProvisioner;
    private readonly FactoryOptions _options = options.Value;
    private readonly ILogger<ComponentGenerator> _logger = logger;

    public async Task<GeneratedComponent> GenerateAsync(
        ComponentName name,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateOperationTimeout(cancellationToken);
        var operationToken = timeout.Token;

        await _gitLab.EnsureProjectNameAvailableAsync(
            _options.NamespaceId,
            name.ProjectPath,
            operationToken);

        using var workspace = await _workspaces.CreateAsync(operationToken);

        await PrepareRepositoryAsync(workspace, name, operationToken);

        var project = await _gitLab.CreateProjectAsync(
            _options.NamespaceId,
            name.ProjectPath,
            operationToken);

        await PushToCreatedProjectAsync(workspace, project, operationToken);

        await ProvisionCdAsync(name, project, operationToken);

        return new GeneratedComponent(
            project.Id,
            project.WebUrl,
            name.Value,
            RepositoryDefaults.Branch);
    }

    private CancellationTokenSource CreateOperationTimeout(CancellationToken requestToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.OperationTimeoutSeconds));

        return timeout;
    }

    private async Task PrepareRepositoryAsync(
        IWorkspace workspace,
        ComponentName name,
        CancellationToken cancellationToken)
    {
        var template = await _gitLab.GetProjectAsync(
            _options.TemplateProjectId,
            cancellationToken);

        await _repository.CloneTemplateAsync(template, workspace, cancellationToken);

        await _customizer.CustomizeAsync(workspace.RepositoryPath, name, cancellationToken);
        await _repository.CreateInitialCommitAsync(workspace, name, cancellationToken);
    }

    private async Task ProvisionCdAsync(
        ComponentName name,
        GitLabProject project,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sensor {Sensor} was created and pushed at {ProjectUrl}; starting CD provisioning", name.Value, project.WebUrl);
        try
        {
            await _cdProvisioner.ProvisionAsync(name, cancellationToken);
            _logger.LogInformation("Sensor {Sensor} and its CD files were published successfully; returning response", name.Value);
        }
        catch (Exception exception)
        {
            var stage = (exception as ComponentFactoryException)?.Stage ?? "cd-prepare";
            _logger.LogError(exception, "Sensor project {ProjectId} was published, but CD provisioning failed at {Stage}", project.Id, stage);

            throw new ComponentFactoryException(
                FactoryError.GenerationIncomplete,
                $"Sensor '{name.Value}' was created and pushed, but CD provisioning failed at '{stage}'. " +
                "The sensor project was retained. Check the logs before retrying; the sensor already exists.",
                project.WebUrl,
                stage,
                exception);
        }
    }

    private async Task PushToCreatedProjectAsync(
        IWorkspace workspace,
        GitLabProject project,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.PushAsync(workspace, project, cancellationToken);
        }
        catch (Exception exception)
        {
            // Preserve the project and avoid logging raw Git output or credentials.
            _logger.LogWarning(
                "Push failed for project {ProjectId}; project retained for manual repair",
                project.Id);

            var message = exception is OperationCanceledException
                ? "Push was cancelled or timed out. The project was retained; check whether the push completed."
                : "Project created, but push failed. The project was retained for manual repair.";

            throw new ComponentFactoryException(
                FactoryError.GenerationIncomplete,
                message,
                project.WebUrl,
                "push",
                exception);
        }
    }
}
