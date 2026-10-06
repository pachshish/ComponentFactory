using System.Diagnostics;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using ComponentFactory.Infrastructure.Git;
using ComponentFactory.Infrastructure.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FactoryChecks;

internal static class GitRepositoryChecks
{
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var temporary = new TestDirectory();
        var options = Options.Create(new FactoryOptions
        {
            WorkRoot = temporary.Path,
            GitLabUrl = "https://gitlab.example",
            Token = "test-token"
        });

        var factory = new WorkspaceFactory(options, NullLogger<WorkspaceFactory>.Instance);
        var runner = new GitCommandRunner(options, NullLogger<GitCommandRunner>.Instance);
        var repository = new GitRepository(runner, new RepositoryUrlValidator(options), NullLogger<GitRepository>.Instance);
        string workspacePath;

        using (var workspace = await factory.CreateAsync(CancellationToken.None))
        {
            workspacePath = workspace.RootPath;
            Directory.CreateDirectory(workspace.RepositoryPath);
            await File.WriteAllTextAsync(System.IO.Path.Combine(workspace.RepositoryPath, "file.txt"), "first");

            Assert.True(!File.ReadAllText(workspace.AskPassPath).Contains("test-token", StringComparison.Ordinal), "askpass contains no token");
            await CheckAskPassAsync(workspace.AskPassPath);
            await CheckFailureDiagnosticsAsync(options, workspace);

            await repository.CreateInitialCommitAsync(workspace, ComponentName.Create("Alfa"), CancellationToken.None);
            await File.WriteAllTextAsync(System.IO.Path.Combine(workspace.RepositoryPath, "file.txt"), "second");
            await runner.RunAsync(workspace.RepositoryPath, workspace.AskPassPath, CancellationToken.None, "add", "--all");
            await runner.RunAsync(workspace.RepositoryPath, workspace.AskPassPath, CancellationToken.None, "commit", "-m", "second old commit");

            Assert.True(await CountCommitsAsync(workspace.RepositoryPath) == "2", "source has old history");

            await repository.CreateInitialCommitAsync(workspace, ComponentName.Create("Bravo"), CancellationToken.None);

            Assert.True(await CountCommitsAsync(workspace.RepositoryPath) == "1", "new repository has one initial commit");
        }

        Assert.True(!Directory.Exists(workspacePath), "temporary workspace disposed");
    }

    private static async Task CheckAskPassAsync(string askPassPath)
    {
        var startInfo = new ProcessStartInfo(askPassPath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("Password for HTTPS:");
        startInfo.Environment["FACTORY_GIT_TOKEN"] = "test-token";

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(output == "test-token\n", "askpass reads token from environment");
    }

    private static async Task CheckFailureDiagnosticsAsync(IOptions<FactoryOptions> options, ComponentFactory.Application.Abstractions.IWorkspace workspace)
    {
        options.Value.ApiKey = "test-api-key";
        var logger = new RecordingLogger();
        var runner = new GitCommandRunner(options, logger);
        var exception = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            runner.RunAsync(workspace.RepositoryPath, workspace.AskPassPath, CancellationToken.None,
                "factory-unknown-command-test-token-test-api-key"));

        Assert.True(exception.InnerException is InvalidOperationException, "Git failure preserves sanitized stderr in its error chain");
        Assert.True(exception.GetBaseException().Message.Contains("Git exit code"), "Git failure includes the exit code");
        var diagnostics = exception.ToString() + string.Join('\n', logger.Messages);
        Assert.True(!diagnostics.Contains("test-token") && !diagnostics.Contains("test-api-key"), "configured token and API key are excluded from diagnostics");
        Assert.True(logger.Messages.Any(message => message.Contains("GitError=")), "Git stderr is included in the error log message");
    }

    private sealed class RecordingLogger : ILogger<GitCommandRunner>
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private static async Task<string> CountCommitsAsync(string repositoryPath)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("rev-list");
        startInfo.ArgumentList.Add("--count");
        startInfo.ArgumentList.Add("HEAD");

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.Trim();
    }
}
