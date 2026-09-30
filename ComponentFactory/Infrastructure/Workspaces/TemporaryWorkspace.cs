using ComponentFactory.Application.Abstractions;

namespace ComponentFactory.Infrastructure.Workspaces;

public sealed class TemporaryWorkspace(string rootPath, ILogger logger) : IWorkspace
{
    private readonly ILogger _logger = logger;
    private bool _disposed;

    public string RootPath { get; } = rootPath;

    public string RepositoryPath { get; } = Path.Combine(rootPath, "repository");

    public string AskPassPath { get; } = Path.Combine(rootPath, "askpass.sh");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup failure must not hide the operation's result.
            _logger.LogWarning(
                "Could not clean temporary workspace {WorkspaceId}",
                Path.GetFileName(RootPath));
        }
    }
}
