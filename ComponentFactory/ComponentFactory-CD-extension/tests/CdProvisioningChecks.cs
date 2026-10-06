using System.Diagnostics;
using ComponentFactory.Application.Abstractions;
using ComponentFactory.Application.Models;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using ComponentFactory.Infrastructure.Cd;
using ComponentFactory.Infrastructure.Git;
using ComponentFactory.Infrastructure.Templates;
using ComponentFactory.Infrastructure.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FactoryChecks;

internal static class CdProvisioningChecks
{
    public static async Task RunAsync()
    {
        CheckConfiguration();
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("SKIP: CD Git integration checks require the existing Linux runtime.");
            return;
        }

        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var provisioner = fixture.CreateProvisioner();

        await provisioner.ProvisionAsync(ComponentName.Create("Bravo"), CancellationToken.None);
        foreach (var role in new[] { "agent", "poller" })
        {
            foreach (var file in Fixture.Files)
            {
                var original = await fixture.ShowAsync($"sensorgates/senortemplate/{role}/{file}");
                var generated = await fixture.ShowAsync($"sensorgates/bravo/{role}/{file}");
                Assert.True(original.Contains("senortemplate"), "CD template remains unchanged");
                Assert.True(generated.Contains("bravo") && !generated.Contains("senortemplate"), "CD sensor names are customized");
                if (file.EndsWith("values.yaml"))
                {
                    Assert.True(generated.Contains("tag: 1.0.0"), "initial image tag stays 1.0.0");
                }
            }
        }

        Assert.True(await fixture.ShowAsync("unrelated.txt") == "do not change", "other CD content is unchanged");
        Assert.True(await RunGitAsync(fixture.Remote, "rev-list", "--count", "release/cd") == "3", "normal CD commit retains both original commits");
        var changed = await RunGitAsync(fixture.Remote, "diff", "--name-only", "release/cd~1", "release/cd");
        Assert.True(changed.Split('\n').Length == 12 && changed.Split('\n').All(path => path.StartsWith("sensorgates/bravo/")), "only 12 new sensor files are committed");
        Assert.True(!Directory.EnumerateFileSystemEntries(fixture.WorkRoot).Any(), "CD workspace is cleaned after success");

        var duplicate = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            provisioner.ProvisionAsync(ComponentName.Create("Bravo"), CancellationToken.None));
        Assert.True(duplicate.Stage == "cd-prepare" && duplicate.InnerException is ComponentFactoryException { Error: FactoryError.ProjectAlreadyExists }, "existing CD folder is rejected without overwriting");

        fixture.Cd.TemplatePath = "sensorgates/missing";
        var missing = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            provisioner.ProvisionAsync(ComponentName.Create("Charlie"), CancellationToken.None));
        Assert.True(missing.Stage == "cd-prepare" && missing.InnerException is ComponentFactoryException { Error: FactoryError.InvalidTemplate }, "missing template reports its preparation failure");
        Assert.True(!Directory.EnumerateFileSystemEntries(fixture.WorkRoot).Any(), "failed CD workspace is cleaned");
        fixture.Cd.TemplatePath = "sensorgates/senortemplate";

        var advancing = new AdvancingRepository(fixture.Repository, fixture);
        await fixture.CreateProvisioner(advancing).ProvisionAsync(ComponentName.Create("Delta"), CancellationToken.None);
        Assert.True(advancing.PushAttempts == 3 && advancing.Refreshes == 2, "two concurrent remote advances are rebased before the third push");
        Assert.True(await fixture.ShowAsync("advance-1.txt") == "remote change 1" && await fixture.ShowAsync("advance-2.txt") == "remote change 2", "concurrent changes survive CD publication");
        Assert.True(await RunGitAsync(fixture.Remote, "rev-list", "--count", "release/cd") == "6", "CD history includes both concurrent changes and the new sensor commit");
    }

    private static void CheckConfiguration()
    {
        var validator = new CdOptionsValidator();
        var options = new CdOptions { ProjectId = 9 };
        Assert.True(validator.Validate(null, options).Succeeded, "valid CD configuration accepted");
        options.ProjectId = 0;
        Assert.True(validator.Validate(null, options).Failed, "unset CD project is rejected at startup");
        options.ProjectId = 9;
        foreach (var path in new[] { "../outside", "/tmp/outside", "sensorgates/.git", "sensorgates/*", "C:\\outside" })
        {
            options.TargetRoot = path;
            Assert.True(validator.Validate(null, options).Failed, "unsafe CD path is rejected");
        }
        options.TargetRoot = "sensorgates";
        options.TemplateName = "senortemplate";
        Assert.True(validator.Validate(null, options).Failed, "CD template name must use the shared PascalCase convention");
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly string[] Files = ["Chart.yaml", "values.yaml", "develop-values.yaml", "integration-values.yaml", "production-values.yaml", "secondary-values.yaml"];
        private readonly TestDirectory _directory = new();
        private readonly Dictionary<string, string?> _previousEnvironment = new();
        private readonly IOptions<FactoryOptions> _factoryOptions;
        private readonly GitCommandRunner _commands;
        public string Seed { get; }
        public string Remote { get; }
        public string WorkRoot { get; }
        public CdOptions Cd { get; } = new() { ProjectId = 9, Branch = "release/cd" };
        public GitRepository Repository { get; }

        public Fixture()
        {
            Seed = _directory.CreateSubdirectory("seed");
            Remote = Path.Combine(_directory.Path, "cd.git");
            WorkRoot = _directory.CreateSubdirectory("workspaces");
            _factoryOptions = Options.Create(new FactoryOptions
            {
                GitLabUrl = "https://gitlab.example", Token = "test-token", WorkRoot = WorkRoot,
                TemplateName = "TemplateSensor"
            });
            _commands = new GitCommandRunner(_factoryOptions, NullLogger<GitCommandRunner>.Instance);
            Repository = new GitRepository(_commands, new RepositoryUrlValidator(_factoryOptions), NullLogger<GitRepository>.Instance);

            // Local-only integration: the URL validator still sees HTTPS, while Git
            // rewrites that exact test host to a temporary file:// remote.
            SetEnvironment("GIT_CONFIG_COUNT", "2");
            SetEnvironment("GIT_CONFIG_KEY_0", $"url.file://{_directory.Path}/.insteadOf");
            SetEnvironment("GIT_CONFIG_VALUE_0", "https://gitlab.example/");
            SetEnvironment("GIT_CONFIG_KEY_1", "protocol.file.allow");
            SetEnvironment("GIT_CONFIG_VALUE_1", "always");
        }

        public async Task InitializeAsync()
        {
            await RunGitAsync(Seed, "init", "--initial-branch=release/cd");
            await RunGitAsync(Seed, "config", "user.name", "CD Checks");
            await RunGitAsync(Seed, "config", "user.email", "checks@example.invalid");
            foreach (var role in new[] { "agent", "poller" })
            {
                var path = Path.Combine(Seed, "sensorgates", "senortemplate", role);
                Directory.CreateDirectory(path);
                foreach (var file in Files)
                {
                    var content = file == "Chart.yaml"
                        ? $"apiVersion: v2\nname: senortemplate-{role}\nversion: 1.0.0\n"
                        : $"generic-chart:\n  applicationName: senortemplate-{role}\n  deployment:\n    image:\n      repository: registry.example/senortemplate-{role}\n      tag: 1.0.0\n  displayName: SenorTemplate\n  upperName: SENORTEMPLATE\n";
                    await File.WriteAllTextAsync(Path.Combine(path, file), content);
                }
            }
            await RunGitAsync(Seed, "add", "--all");
            await RunGitAsync(Seed, "commit", "-m", "existing CD template");
            await File.WriteAllTextAsync(Path.Combine(Seed, "unrelated.txt"), "do not change");
            await RunGitAsync(Seed, "add", "--all");
            await RunGitAsync(Seed, "commit", "-m", "existing unrelated content");
            await RunGitAsync(_directory.Path, "clone", "--bare", Seed, Remote);
        }

        public CdProvisioner CreateProvisioner(IGitRepository? repository = null)
        {
            return new CdProvisioner(
                new ReadOnlyGitLab(), repository ?? Repository,
                new TemplateCustomizer(new TemplateScanner(), new Utf8TextRewriter(), _factoryOptions, _commands, NullLogger<TemplateCustomizer>.Instance),
                new WorkspaceFactory(_factoryOptions, NullLogger<WorkspaceFactory>.Instance),
                new TemplateScanner(), Options.Create(Cd), NullLogger<CdProvisioner>.Instance);
        }

        public Task<string> ShowAsync(string path) => RunGitAsync(Remote, "show", $"release/cd:{path}");

        public async Task AdvanceRemoteAsync(int number)
        {
            var writer = Path.Combine(_directory.Path, $"writer-{number}");
            await RunGitAsync(_directory.Path, "clone", "--branch", "release/cd", Remote, writer);
            await RunGitAsync(writer, "config", "user.name", "Other CD Writer");
            await RunGitAsync(writer, "config", "user.email", "other@example.invalid");
            await File.WriteAllTextAsync(Path.Combine(writer, $"advance-{number}.txt"), $"remote change {number}");
            await RunGitAsync(writer, "add", "--all");
            await RunGitAsync(writer, "commit", "-m", $"concurrent change {number}");
            await RunGitAsync(writer, "push", "origin", "release/cd");
        }

        private void SetEnvironment(string key, string value)
        {
            _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previousEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
            _directory.Dispose();
        }
    }

    private sealed class ReadOnlyGitLab : IGitLabClient
    {
        public Task<GitLabProject> GetProjectAsync(long projectId, CancellationToken cancellationToken)
        {
            Assert.True(projectId == 9, "CD accesses the configured existing project");
            return Task.FromResult(new GitLabProject(9, "https://gitlab.example/cd", "https://gitlab.example/cd.git"));
        }

        public Task EnsureProjectNameAvailableAsync(long namespaceId, string projectPath, CancellationToken cancellationToken)
            => throw new InvalidOperationException("CD must not check for a new remote project.");

        public Task<GitLabProject> CreateProjectAsync(long namespaceId, string projectPath, CancellationToken cancellationToken)
            => throw new InvalidOperationException("CD must not create a remote project.");
    }

    private sealed class AdvancingRepository(IGitRepository inner, Fixture fixture) : IGitRepository
    {
        public int PushAttempts { get; private set; }
        public int Refreshes { get; private set; }

        public async Task PushAsync(IWorkspace workspace, GitLabProject target, string branch, CancellationToken cancellationToken)
        {
            PushAttempts++;
            if (PushAttempts <= 2)
            {
                await fixture.AdvanceRemoteAsync(PushAttempts);
            }
            await inner.PushAsync(workspace, target, branch, cancellationToken);
        }

        public Task RefreshBranchAsync(IWorkspace workspace, string branch, CancellationToken cancellationToken)
        {
            Refreshes++;
            return inner.RefreshBranchAsync(workspace, branch, cancellationToken);
        }

        public Task CloneAsync(GitLabProject project, IWorkspace workspace, string branch, CancellationToken cancellationToken)
            => inner.CloneAsync(project, workspace, branch, cancellationToken);
        public Task CommitAsync(IWorkspace workspace, string message, string relativePath, CancellationToken cancellationToken)
            => inner.CommitAsync(workspace, message, relativePath, cancellationToken);
        public Task CloneTemplateAsync(GitLabProject template, IWorkspace workspace, CancellationToken cancellationToken)
            => inner.CloneTemplateAsync(template, workspace, cancellationToken);
        public Task CreateInitialCommitAsync(IWorkspace workspace, ComponentName name, CancellationToken cancellationToken)
            => inner.CreateInitialCommitAsync(workspace, name, cancellationToken);
        public Task PushAsync(IWorkspace workspace, GitLabProject target, CancellationToken cancellationToken)
            => inner.PushAsync(workspace, target, cancellationToken);
    }

    private static async Task<string> RunGitAsync(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Test Git command failed: {error}");
        }
        return output.Trim();
    }
}
