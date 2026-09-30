using ComponentFactory.Application.Abstractions;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Infrastructure.Workspaces;

public sealed class WorkspaceFactory(
    IOptions<FactoryOptions> options,
    ILogger<WorkspaceFactory> logger) : IWorkspaceFactory
{
    private const string AskPassScript = """
        #!/bin/sh
        case "$1" in
            *Username*) printf '%s\n' 'oauth2' ;;
            *) printf '%s\n' "$FACTORY_GIT_TOKEN" ;;
        esac
        """;

    private readonly FactoryOptions _options = options.Value;
    private readonly ILogger<WorkspaceFactory> _logger = logger;

    public async Task<IWorkspace> CreateAsync(CancellationToken cancellationToken)
    {
        EnsureLinuxRuntime();
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(_options.WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        var workspace = new TemporaryWorkspace(path, _logger);

        try
        {
            await WriteAuthenticationHelperAsync(workspace, cancellationToken);

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task WriteAuthenticationHelperAsync(
        IWorkspace workspace,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(workspace.AskPassPath, AskPassScript + "\n", cancellationToken);

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                workspace.AskPassPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void EnsureLinuxRuntime()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new ComponentFactoryException(
                FactoryError.ExternalOperationFailed,
                "Run this service in the supplied Linux container.");
        }
    }
}
