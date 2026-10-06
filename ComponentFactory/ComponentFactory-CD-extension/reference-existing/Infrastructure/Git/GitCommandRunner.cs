using System.Diagnostics;
using System.Text.RegularExpressions;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Infrastructure.Git;

public sealed class GitCommandRunner(IOptions<FactoryOptions> options, ILogger<GitCommandRunner> logger)
{
    private readonly FactoryOptions _options = options.Value;
    private readonly ILogger<GitCommandRunner> _logger = logger;

    public async Task RunAsync(
        string workingDirectory,
        string askPassPath,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        if (arguments.Length == 0)
        {
            throw new ArgumentException("A Git operation is required.", nameof(arguments));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var operation = SanitizeDiagnostic(arguments[0]);
        var timer = Stopwatch.StartNew();
        _logger.LogDebug("Starting Git operation {Operation}", operation);
        var startInfo = CreateStartInfo(workingDirectory, askPassPath, arguments);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("The Git process could not be started.");
        }
        catch (Exception exception)
        {
            var details = SanitizeDiagnostic(exception.Message);
            _logger.LogError("Git operation {Operation} could not start: {Details}", operation, details);
            throw CommandFailure(operation, details);
        }

        using (process)
        {
            // Drain both pipes concurrently so Git cannot block on a full output buffer.
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            try
            {
                await WaitForExitAsync(process, standardOutput, standardError, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Git operation {Operation} was cancelled or timed out after {ElapsedMs} ms", operation, timer.ElapsedMilliseconds);
                throw;
            }

            if (process.ExitCode != 0)
            {
                // Keep useful diagnostics in the log and exception chain, after
                // removing configured credentials and URL/header credentials.
                var details = SanitizeDiagnostic(await standardError);
                if (string.IsNullOrWhiteSpace(details))
                {
                    details = "Git returned no stderr output.";
                }
                _logger.LogError(
                    "Git operation {Operation} failed with exit code {ExitCode} after {ElapsedMs} ms; GitError={GitError}",
                    operation, process.ExitCode, timer.ElapsedMilliseconds, details);
                throw CommandFailure(operation, $"Git exit code {process.ExitCode}: {details}");
            }

            _logger.LogDebug("Git operation {Operation} completed in {ElapsedMs} ms", operation, timer.ElapsedMilliseconds);
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

    private string SanitizeDiagnostic(string text)
    {
        foreach (var secret in new[] { _options.Token, _options.ApiKey }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => value.Length))
        {
            text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        text = Regex.Replace(text, @"(?<scheme>[a-z][a-z0-9+.-]*://)[^\s/]*@", "${scheme}[REDACTED]@", RegexOptions.IgnoreCase);
        text = Regex.Replace(text,
            @"(?<header>Authorization\s*:\s*)(?:Basic|Bearer)\s+[^\s]+",
            "${header}[REDACTED]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text,
            @"(?<key>PRIVATE-TOKEN|X-Api-Key|access_token|private_token)(?<separator>\s*[:=]\s*)[^\s&]+",
            "${key}${separator}[REDACTED]", RegexOptions.IgnoreCase);

        const int maxCharacters = 4000;
        return text.Length <= maxCharacters ? text : text[..maxCharacters] + " [truncated]";
    }

    private static ComponentFactoryException CommandFailure(string operation, string details)
    {
        return new ComponentFactoryException(
            FactoryError.ExternalOperationFailed,
            $"Git operation '{operation}' failed.",
            innerException: new InvalidOperationException(details));
    }
}
