using System.Diagnostics;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Infrastructure.Git;

public sealed class GitCommandRunner(IOptions<FactoryOptions> options)
{
    private readonly FactoryOptions _options = options.Value;

    public async Task RunAsync(
        string workingDirectory,
        string askPassPath,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = CreateStartInfo(workingDirectory, askPassPath, arguments);

        using var process = Process.Start(startInfo)
            ?? throw CommandFailure(arguments[0]);

        // Drain both pipes concurrently so Git cannot block on a full output buffer.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        await WaitForExitAsync(process, standardOutput, standardError, cancellationToken);

        if (process.ExitCode != 0)
        {
            // Raw Git output may contain credentials or private repository details.
            throw CommandFailure(arguments[0]);
        }
    }

    private ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        string askPassPath,
        IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        AddConfigurationOverride(startInfo, "credential.helper=");
        AddConfigurationOverride(startInfo, "core.hooksPath=/dev/null");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ConfigureEnvironment(startInfo, askPassPath);

        return startInfo;
    }

    private void ConfigureEnvironment(ProcessStartInfo startInfo, string askPassPath)
    {
        startInfo.Environment["GIT_ASKPASS"] = askPassPath;
        startInfo.Environment["FACTORY_GIT_TOKEN"] = _options.Token;
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
    }

    private static void AddConfigurationOverride(ProcessStartInfo startInfo, string value)
    {
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(value);
    }

    private static async Task WaitForExitAsync(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutput, standardError);

            throw;
        }

        await Task.WhenAll(standardOutput, standardError);
    }

    private static ComponentFactoryException CommandFailure(string operation)
    {
        return new ComponentFactoryException(
            FactoryError.ExternalOperationFailed,
            $"Git operation '{operation}' failed.");
    }
}
