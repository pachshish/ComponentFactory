using ComponentFactory.Application;
using ComponentFactory.Application.Abstractions;
using ComponentFactory.Application.Models;
using ComponentFactory.Domain;

namespace ComponentFactory.Infrastructure.Git;

public sealed class GitRepository(GitCommandRunner commands, RepositoryUrlValidator urls, ILogger<GitRepository> logger) : IGitRepository
{
    private readonly GitCommandRunner _commands = commands;
    private readonly RepositoryUrlValidator _urls = urls;
    private readonly ILogger<GitRepository> _logger = logger;

    public Task CloneTemplateAsync(
        GitLabProject template,
        IWorkspace workspace,
        CancellationToken cancellationToken)
    {
        return CloneAsync(template, workspace, RepositoryDefaults.Branch, cancellationToken);
    }

    public async Task CloneAsync(
        GitLabProject project,
        IWorkspace workspace,
        string branch,
        CancellationToken cancellationToken)
    {
        _urls.Validate(project.RepositoryUrl);
        _logger.LogDebug("Cloning GitLab project {ProjectId} on branch {Branch}", project.Id, branch);
        await _commands.RunAsync(
            workspace.RootPath, workspace.AskPassPath, cancellationToken,
            "check-ref-format", "--branch", branch);

        await _commands.RunAsync(
            workspace.RootPath,
            workspace.AskPassPath,
            cancellationToken,
            "clone",
            "--recurse-submodules",
            "--depth", "1",
            "--single-branch",
            "--branch", branch,
            "--", project.RepositoryUrl, workspace.RepositoryPath);
        _logger.LogDebug("Git clone completed for project {ProjectId} on branch {Branch}", project.Id, branch);
    }

    public async Task CreateInitialCommitAsync(
        IWorkspace workspace,
        ComponentName name,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating initial sensor commit for {Sensor}", name.Value);
        if (Directory.Exists(Path.Combine(workspace.RepositoryPath, ".git")))
        {
            // Keep the Git index and submodule repositories; publish a root commit
            // whose history is independent of the template.
            await RunInRepositoryAsync(
                workspace, cancellationToken,
                "checkout", "--orphan", $"factory-initial-{Guid.NewGuid():N}",
                "--no-recurse-submodules");
        }
        else
        {
            await RunInRepositoryAsync(workspace, cancellationToken, "init", $"--initial-branch={RepositoryDefaults.Branch}");
        }
        await CommitAsync(workspace, $"Initialize {name.Value} from component template", ".", cancellationToken);
        await RunInRepositoryAsync(workspace, cancellationToken, "branch", "-M", RepositoryDefaults.Branch);
    }

    public async Task CommitAsync(
        IWorkspace workspace,
        string message,
        string relativePath,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Staging Git changes under {RelativePath} and creating commit", relativePath);
        await ConfigureCommitAuthorAsync(workspace, cancellationToken);
        await RunInRepositoryAsync(workspace, cancellationToken, "add", "--all", "--", relativePath);
        await RunInRepositoryAsync(workspace, cancellationToken, "commit", "-m", message);
        _logger.LogDebug("Git commit completed for path {RelativePath}", relativePath);
    }

    public Task PushAsync(
        IWorkspace workspace,
        GitLabProject target,
        CancellationToken cancellationToken)
    {
        return PushAsync(workspace, target, RepositoryDefaults.Branch, cancellationToken);
    }

    public async Task PushAsync(
        IWorkspace workspace,
        GitLabProject target,
        string branch,
        CancellationToken cancellationToken)
    {
        _urls.Validate(target.RepositoryUrl);
        _logger.LogDebug("Pushing GitLab project {ProjectId} to branch {Branch}", target.Id, branch);

        // A cloned repository already has origin. This also works for a fresh
        // repository, where setting this key creates the remote configuration.
        await RunInRepositoryAsync(workspace, cancellationToken, "config", "remote.origin.url", target.RepositoryUrl);
        await RunInRepositoryAsync(workspace, cancellationToken, "push", "--set-upstream", "origin", $"HEAD:refs/heads/{branch}");
        _logger.LogDebug("Git push completed for project {ProjectId} on branch {Branch}", target.Id, branch);
    }

    public async Task RefreshBranchAsync(
        IWorkspace workspace,
        string branch,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching latest remote branch {Branch} before rebasing the local CD commit", branch);
        // A shallow clone needs the intervening history for a reliable rebase.
        var arguments = new List<string> { "fetch", "--no-recurse-submodules" };
        if (File.Exists(Path.Combine(workspace.RepositoryPath, ".git", "shallow")))
        {
            arguments.Add("--unshallow");
        }
        arguments.AddRange(["origin", branch]);
        await RunInRepositoryAsync(workspace, cancellationToken, arguments.ToArray());
        _logger.LogInformation("Fetch completed for {Branch}; starting rebase onto FETCH_HEAD", branch);
        await RunInRepositoryAsync(workspace, cancellationToken, "rebase", "FETCH_HEAD");
        _logger.LogInformation("Rebase completed for {Branch}", branch);
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
