using System.Diagnostics;
using ComponentFactory.Application.Abstractions;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using ComponentFactory.Infrastructure.Templates;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Infrastructure.Cd;

public sealed class CdProvisioner(
    IGitLabClient gitLab,
    IGitRepository repository,
    ITemplateCustomizer customizer,
    IWorkspaceFactory workspaces,
    TemplateScanner scanner,
    IOptions<CdOptions> options,
    ILogger<CdProvisioner> logger) : ICdProvisioner
{
    private static readonly string[] Roles = ["agent", "poller"];
    private static readonly string[] RequiredFiles =
    [
        "Chart.yaml", "values.yaml", "develop-values.yaml", "integration-values.yaml",
        "production-values.yaml", "secondary-values.yaml"
    ];

    private readonly IGitLabClient _gitLab = gitLab;
    private readonly IGitRepository _repository = repository;
    private readonly ITemplateCustomizer _customizer = customizer;
    private readonly IWorkspaceFactory _workspaces = workspaces;
    private readonly TemplateScanner _scanner = scanner;
    private readonly CdOptions _options = options.Value;
    private readonly ILogger<CdProvisioner> _logger = logger;

    public async Task ProvisionAsync(ComponentName name, CancellationToken cancellationToken)
    {
        var stage = "cd-prepare";
        var timer = Stopwatch.StartNew();
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Sensor"] = name.Value,
            ["CdProjectId"] = _options.ProjectId,
            ["CdBranch"] = _options.Branch
        });

        try
        {
            _logger.LogInformation("Preparing CD files for {Sensor} in project {CdProjectId}", name.Value, _options.ProjectId);

            using var workspace = await _workspaces.CreateAsync(cancellationToken);
            _logger.LogDebug("CD workspace allocated for {Sensor}: {WorkspacePath}", name.Value, workspace.RootPath);
            _logger.LogInformation("Reading existing CD project {CdProjectId} for {Sensor}", _options.ProjectId, name.Value);
            var project = await _gitLab.GetProjectAsync(_options.ProjectId, cancellationToken);

            _logger.LogInformation("Cloning CD project {CdProjectId}, branch {Branch}, for {Sensor}", project.Id, _options.Branch, name.Value);
            await _repository.CloneAsync(project, workspace, _options.Branch, cancellationToken);
            _logger.LogInformation("CD clone completed for {Sensor} on {Branch}", name.Value, _options.Branch);

            var templatePath = ResolveRepositoryPath(workspace.RepositoryPath, _options.TemplatePath);
            var relativeTarget = $"{_options.TargetRoot}/{name.ProjectPath}";
            var targetPath = ResolveRepositoryPath(workspace.RepositoryPath, relativeTarget);

            EnsureDistinctPaths(templatePath, targetPath);

            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                _logger.LogWarning("CD target {CdPath} already exists for {Sensor}; refusing to overwrite", relativeTarget, name.Value);
                throw new ComponentFactoryException(
                    FactoryError.ProjectAlreadyExists,
                    $"CD path '{relativeTarget}' already exists; it was not overwritten.");
            }

            _logger.LogInformation("Copying CD template {TemplatePath} to {CdPath} for {Sensor}", _options.TemplatePath, relativeTarget, name.Value);
            var copiedFiles = await CopyTemplateAsync(templatePath, targetPath, cancellationToken);
            _logger.LogInformation("Copied {FileCount} CD files for {Sensor}; customizing template name {TemplateName}", copiedFiles, name.Value, _options.TemplateName);
            await _customizer.CustomizeAsync(targetPath, name, _options.TemplateName, cancellationToken);
            _logger.LogInformation("CD customization completed for {Sensor} at {CdPath}", name.Value, relativeTarget);

            stage = "cd-commit";
            _logger.LogInformation("Creating CD commit for {Sensor}; staging only {CdPath}", name.Value, relativeTarget);
            await _repository.CommitAsync(
                workspace,
                $"Add CD charts for {name.Value}",
                relativeTarget,
                cancellationToken);
            _logger.LogInformation("CD commit completed for {Sensor}", name.Value);

            stage = "cd-push";
            await PublishAsync(workspace, project, name, cancellationToken);

            _logger.LogInformation("CD files published for {Sensor} at {CdPath} on {Branch} in {ElapsedMs} ms", name.Value, relativeTarget, _options.Branch, timer.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "CD provisioning failed for {Sensor} at {Stage} after {ElapsedMs} ms; RootCause={RootCause}; Details={Details}",
                name.Value, stage, timer.ElapsedMilliseconds, exception.GetBaseException().Message, exception.ToString());
            throw new ComponentFactoryException(
                FactoryError.ExternalOperationFailed,
                $"CD provisioning for '{name.Value}' failed at '{stage}': {exception.Message}",
                stage: stage,
                innerException: exception);
        }
    }

    private async Task PublishAsync(
        IWorkspace workspace,
        ComponentFactory.Application.Models.GitLabProject project,
        ComponentName name,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _logger.LogInformation("Publishing CD files for {Sensor}, project {CdProjectId}, on {Branch}; attempt {Attempt}/{MaxAttempts}", name.Value, project.Id, _options.Branch, attempt, maxAttempts);
                await _repository.PushAsync(workspace, project, _options.Branch, cancellationToken);
                return;
            }
            catch (ComponentFactoryException exception) when (
                attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                // The runner does not classify Git stderr. Refresh/rebase can resolve
                // a concurrent push; authentication/network/conflict failures still
                // fail explicitly. Never force-push the shared CD repository.
                _logger.LogWarning(exception, "CD push attempt {Attempt} failed for {Sensor}; refreshing {Branch} before retry", attempt, name.Value, _options.Branch);
                await _repository.RefreshBranchAsync(workspace, _options.Branch, cancellationToken);
                _logger.LogInformation("CD branch {Branch} refreshed and local commit rebased for {Sensor}; retrying publication", _options.Branch, name.Value);
            }
        }
    }

    private async Task<int> CopyTemplateAsync(string source, string target, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
        {
            throw InvalidTemplate($"CD template directory '{_options.TemplatePath}' was not found.");
        }

        if (File.Exists(Path.Combine(source, ".git")) || Directory.Exists(Path.Combine(source, ".git")))
        {
            throw InvalidTemplate("The CD chart template must be an ordinary directory, not a repository or submodule root.");
        }

        // Reuse the scanner's validation before creating any copied files.
        var entries = _scanner.Scan(source, cancellationToken);
        var scannedPaths = entries.ToHashSet(StringComparer.Ordinal);

        foreach (var role in Roles)
        {
            foreach (var fileName in RequiredFiles)
            {
                var path = Path.Combine(source, role, fileName);
                if (!File.Exists(path) || !scannedPaths.Contains(path))
                {
                    throw InvalidTemplate($"CD template is missing '{role}/{fileName}'.");
                }
            }
        }

        Directory.CreateDirectory(target);
        var fileCount = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetRelativePath(source, entry));

            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            await using var input = File.OpenRead(entry);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken);
            fileCount++;
            _logger.LogDebug("Copied CD template file {RelativeFile}", Path.GetRelativePath(source, entry));
        }
        return fileCount;
    }

    private static string ResolveRepositoryPath(string repositoryPath, string relativePath)
    {
        var root = Path.GetFullPath(repositoryPath);
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, path);

        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
        {
            throw InvalidTemplate("CD paths must stay inside the cloned repository.");
        }

        // Reject symlinks in the entire existing path, including the target's parent.
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw InvalidTemplate("CD template and target paths must not traverse symbolic links.");
            }
        }

        return path;
    }

    private static void EnsureDistinctPaths(string source, string target)
    {
        if (source == target || source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw InvalidTemplate("CD template and target must be separate directories; neither may contain the other.");
        }
    }

    private static ComponentFactoryException InvalidTemplate(string message)
    {
        return new ComponentFactoryException(FactoryError.InvalidTemplate, message);
    }
}
