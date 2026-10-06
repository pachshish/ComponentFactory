using ComponentFactory.Application;
using ComponentFactory.Application.Abstractions;
using ComponentFactory.Application.Models;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FactoryChecks;

internal static class GenerationChecks
{
    public static async Task RunAsync()
    {
        await CheckSuccessfulGenerationAsync();
        await CheckEarlyFailuresAsync();
        await CheckPushFailureAsync(cancelled: false);
        await CheckPushFailureAsync(cancelled: true);
        await CheckCdFailureAsync();
    }

    private static async Task CheckSuccessfulGenerationAsync()
    {
        var scenario = new Scenario();
        var result = await scenario.Generator.GenerateAsync(ComponentName.Create("Bravo"), CancellationToken.None);

        Assert.True(result.ComponentName == "Bravo" && result.Branch == "master", "generation returns requested component and branch");
        Assert.True(scenario.Fakes.ProjectPath == "bravo", "generation creates lowercase project");
        Assert.True(scenario.Fakes.Workspace.Disposed, "success cleans workspace");
        Assert.True(
            scenario.Fakes.Calls.SequenceEqual(new[] { "check", "workspace", "template", "clone", "customize", "commit", "create", "push", "cd" }),
            "sensor is created and pushed before CD provisioning and the response");
    }

    private static async Task CheckEarlyFailuresAsync()
    {
        var conflict = new Scenario();
        conflict.Fakes.FailAt = "check";

        await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            conflict.Generator.GenerateAsync(ComponentName.Create("Bravo"), CancellationToken.None));

        Assert.True(conflict.Fakes.Calls.SequenceEqual(new[] { "check" }), "existing project stops before allocating workspace");

        var invalidTemplate = new Scenario();
        invalidTemplate.Fakes.FailAt = "customize";

        await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            invalidTemplate.Generator.GenerateAsync(ComponentName.Create("Bravo"), CancellationToken.None));

        Assert.True(!invalidTemplate.Fakes.Calls.Contains("create"), "template failure creates no remote project");
        Assert.True(invalidTemplate.Fakes.Workspace.Disposed, "template failure cleans workspace");

        var creationFailure = new Scenario();
        creationFailure.Fakes.FailAt = "create";

        await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            creationFailure.Generator.GenerateAsync(ComponentName.Create("Bravo"), CancellationToken.None));

        Assert.True(!creationFailure.Fakes.Calls.Contains("push"), "failed project creation prevents push");
        Assert.True(creationFailure.Fakes.Workspace.Disposed, "creation failure cleans workspace");
    }

    private static async Task CheckPushFailureAsync(bool cancelled)
    {
        var scenario = new Scenario();
        scenario.Fakes.FailAt = "push";
        scenario.Fakes.CancelPush = cancelled;

        var exception = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            scenario.Generator.GenerateAsync(ComponentName.Create("Bravo"), CancellationToken.None));

        var reason = cancelled ? "cancelled push" : "failed push";

        Assert.True(exception.Error == FactoryError.GenerationIncomplete, $"{reason} reports incomplete generation");
        Assert.True(exception.ProjectUrl == scenario.Fakes.Project.WebUrl && exception.Stage == "push", $"{reason} includes retained project URL");
        Assert.True(scenario.Fakes.Workspace.Disposed, $"{reason} cleans workspace");
        Assert.True(!scenario.Fakes.Calls.Contains("cd"), $"{reason} prevents CD provisioning");
    }

    private static async Task CheckCdFailureAsync()
    {
        var scenario = new Scenario();
        scenario.Fakes.FailAt = "cd";
        var exception = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            scenario.Generator.GenerateAsync(ComponentName.Create("Bravo"), CancellationToken.None));

        Assert.True(exception.Error == FactoryError.GenerationIncomplete, "CD failure reports partially completed generation");
        Assert.True(exception.ProjectUrl == scenario.Fakes.Project.WebUrl && exception.Stage == "cd-push", "CD failure includes the published sensor URL and precise stage");
        Assert.True(exception.InnerException?.InnerException is IOException, "CD failure retains the original error chain");
        Assert.True(scenario.Fakes.Calls.TakeLast(2).SequenceEqual(new[] { "push", "cd" }), "CD failure happens after sensor push");
        Assert.True(scenario.Fakes.Workspace.Disposed, "CD failure cleans the sensor workspace");
    }

    private sealed class Scenario
    {
        public Scenario()
        {
            Fakes = new GenerationFakes();

            Generator = new ComponentGenerator(
                Fakes,
                Fakes,
                Fakes,
                Fakes,
                Fakes,
                Options.Create(new FactoryOptions
                {
                    NamespaceId = 456,
                    TemplateProjectId = 123,
                    OperationTimeoutSeconds = 30
                }),
                NullLogger<ComponentGenerator>.Instance);
        }

        public GenerationFakes Fakes { get; }

        public ComponentGenerator Generator { get; }
    }

    private sealed class GenerationFakes : IGitLabClient, IGitRepository, ITemplateCustomizer, IWorkspaceFactory, ICdProvisioner
    {
        public List<string> Calls { get; } = [];

        public FakeWorkspace Workspace { get; } = new();

        public GitLabProject Project { get; } = new(9, "https://gitlab.example/bravo", "https://gitlab.example/bravo.git");

        public string? FailAt { get; set; }

        public bool CancelPush { get; set; }

        public string? ProjectPath { get; private set; }

        public Task EnsureProjectNameAvailableAsync(long namespaceId, string projectPath, CancellationToken cancellationToken)
        {
            Step("check", cancellationToken);
            return Task.CompletedTask;
        }

        public Task<GitLabProject> GetProjectAsync(long projectId, CancellationToken cancellationToken)
        {
            Step("template", cancellationToken);
            return Task.FromResult(Project);
        }

        public Task<GitLabProject> CreateProjectAsync(long namespaceId, string projectPath, CancellationToken cancellationToken)
        {
            Step("create", cancellationToken);
            ProjectPath = projectPath;
            return Task.FromResult(Project);
        }

        public Task<IWorkspace> CreateAsync(CancellationToken cancellationToken)
        {
            Step("workspace", cancellationToken);
            return Task.FromResult<IWorkspace>(Workspace);
        }

        public Task CloneTemplateAsync(GitLabProject template, IWorkspace workspace, CancellationToken cancellationToken)
        {
            Step("clone", cancellationToken);
            return Task.CompletedTask;
        }

        public Task CustomizeAsync(string repositoryPath, ComponentName name, CancellationToken cancellationToken)
        {
            Step("customize", cancellationToken);
            return Task.CompletedTask;
        }

        public Task CreateInitialCommitAsync(IWorkspace workspace, ComponentName name, CancellationToken cancellationToken)
        {
            Step("commit", cancellationToken);
            return Task.CompletedTask;
        }

        public Task PushAsync(IWorkspace workspace, GitLabProject target, CancellationToken cancellationToken)
        {
            Step("push", cancellationToken);
            return Task.CompletedTask;
        }

        public Task ProvisionAsync(ComponentName name, CancellationToken cancellationToken)
        {
            Calls.Add("cd");
            cancellationToken.ThrowIfCancellationRequested();
            if (FailAt == "cd")
            {
                throw new ComponentFactoryException(
                    FactoryError.ExternalOperationFailed, "CD push failed", stage: "cd-push",
                    innerException: new IOException("Simulated original failure"));
            }
            return Task.CompletedTask;
        }

        public Task CloneAsync(GitLabProject project, IWorkspace workspace, string branch, CancellationToken cancellationToken)
            => throw new NotSupportedException("Sensor flow must use its existing template clone entry point.");

        public Task CommitAsync(IWorkspace workspace, string message, string relativePath, CancellationToken cancellationToken)
            => throw new NotSupportedException("Sensor flow must use its existing initial commit entry point.");

        public Task PushAsync(IWorkspace workspace, GitLabProject target, string branch, CancellationToken cancellationToken)
            => throw new NotSupportedException("Sensor flow must use its existing push entry point.");

        public Task RefreshBranchAsync(IWorkspace workspace, string branch, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task CustomizeAsync(string rootPath, ComponentName name, string templateName, CancellationToken cancellationToken)
            => throw new NotSupportedException("Sensor flow must use its existing default template name entry point.");

        private void Step(string stage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(stage);

            if (stage != FailAt)
            {
                return;
            }

            if (stage == "push" && CancelPush)
            {
                throw new OperationCanceledException();
            }

            var error = stage == "check"
                ? FactoryError.ProjectAlreadyExists
                : FactoryError.ExternalOperationFailed;

            throw new ComponentFactoryException(error, "Simulated failure");
        }
    }

    private sealed class FakeWorkspace : IWorkspace
    {
        public string RootPath => "/tmp/fake";

        public string RepositoryPath => "/tmp/fake/repository";

        public string AskPassPath => "/tmp/fake/askpass.sh";

        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
