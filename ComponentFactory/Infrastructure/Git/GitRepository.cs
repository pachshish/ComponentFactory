using ComponentFactory.Application;
using ComponentFactory.Application.Abstractions;
using ComponentFactory.Application.Models;
using ComponentFactory.Domain;

namespace ComponentFactory.Infrastructure.Git;

public sealed class GitRepository(GitCommandRunner commands, RepositoryUrlValidator urls) : IGitRepository
{
    private readonly GitCommandRunner _commands = commands;
    private readonly RepositoryUrlValidator _urls = urls;

    public async Task CloneTemplateAsync(
        GitLabProject template,
        IWorkspace workspace,
        CancellationToken cancellationToken)
    {
        _urls.Validate(template.RepositoryUrl);

        await _commands.RunAsync(
            workspace.RootPath,
            workspace.AskPassPath,
            cancellationToken,
            "clone",
            "--depth", "1",
            "--single-branch",
            "--branch", RepositoryDefaults.Branch,
            "--", template.RepositoryUrl, workspace.RepositoryPath);
    }

    public void RemoveTemplateHistory(IWorkspace workspace)
    {
        var gitDirectory = Path.Combine(workspace.RepositoryPath, ".git");
        Directory.Delete(gitDirectory, recursive: true);
    }

    public async Task CreateInitialCommitAsync(
        IWorkspace workspace,
        ComponentName name,
        CancellationToken cancellationToken)
    {
        await RunInRepositoryAsync(workspace, cancellationToken, "init", $"--initial-branch={RepositoryDefaults.Branch}");
        await ConfigureCommitAuthorAsync(workspace, cancellationToken);
        await RunInRepositoryAsync(workspace, cancellationToken, "add", "--all");
        await RunInRepositoryAsync(workspace, cancellationToken, "commit", "-m", $"Initialize {name.Value} from component template");
    }

    public async Task PushAsync(
        IWorkspace workspace,
        GitLabProject target,
        CancellationToken cancellationToken)
    {
        _urls.Validate(target.RepositoryUrl);

        await RunInRepositoryAsync(workspace, cancellationToken, "remote", "add", "origin", target.RepositoryUrl);
        await RunInRepositoryAsync(workspace, cancellationToken, "push", "--set-upstream", "origin", RepositoryDefaults.Branch);
    }

    private async Task ConfigureCommitAuthorAsync(
        IWorkspace workspace,
        CancellationToken cancellationToken)
    {
        await RunInRepositoryAsync(workspace, cancellationToken, "config", "user.name", "Component Factory");
        await RunInRepositoryAsync(workspace, cancellationToken, "config", "user.email", "component-factory@example.invalid");
    }

    private Task RunInRepositoryAsync(
        IWorkspace workspace,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        return _commands.RunAsync(
            workspace.RepositoryPath,
            workspace.AskPassPath,
            cancellationToken,
            arguments);
    }
}
