using ComponentFactory.Application.Abstractions;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using ComponentFactory.Infrastructure.Git;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Infrastructure.Templates;

public sealed class TemplateCustomizer(
    TemplateScanner scanner,
    Utf8TextRewriter textRewriter,
    IOptions<FactoryOptions> options,
    GitCommandRunner commands,
    ILogger<TemplateCustomizer> logger) : ITemplateCustomizer
{
    private readonly TemplateScanner _scanner = scanner;
    private readonly Utf8TextRewriter _textRewriter = textRewriter;
    private readonly string _templateName = options.Value.TemplateName;
    private readonly GitCommandRunner _commands = commands;
    private readonly ILogger<TemplateCustomizer> _logger = logger;

    public Task CustomizeAsync(
        string repositoryPath,
        ComponentName name,
        CancellationToken cancellationToken)
    {
        return CustomizeAsync(repositoryPath, name, _templateName, cancellationToken);
    }

    public async Task CustomizeAsync(
        string repositoryPath,
        ComponentName name,
        string templateName,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Customizing template {TemplateName} for {Sensor} under {RootPath}", templateName, name.Value, repositoryPath);
        var entries = _scanner.Scan(repositoryPath, cancellationToken, out var submodulePaths);
        _logger.LogDebug("Template scan found {EntryCount} entries; preserving {SubmoduleCount} submodule roots", entries.Count, submodulePaths.Count);
        var replacement = new NameReplacement(templateName, name);
        var renamePlan = RenamePlan.Create(repositoryPath, entries, replacement, submodulePaths);

        await RewriteFileContentsAsync(entries, replacement, cancellationToken);

        if (Directory.Exists(Path.Combine(repositoryPath, ".git")))
        {
            // Git updates the index and .gitmodules paths when an enclosing
            // directory moves, keeping submodule contents and commit IDs intact.
            await renamePlan.ApplyAsync(
                cancellationToken,
                (source, target, token) => _commands.RunAsync(
                    repositoryPath,
                    string.Empty,
                    token,
                    "mv", "--",
                    Path.GetRelativePath(repositoryPath, source),
                    Path.GetRelativePath(repositoryPath, target)));
        }
        else
        {
            await renamePlan.ApplyAsync(cancellationToken);
        }
        _logger.LogDebug("Template customization completed for {Sensor} under {RootPath}", name.Value, repositoryPath);
    }

    private async Task RewriteFileContentsAsync(
        IEnumerable<string> entries,
        NameReplacement replacement,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in entries.Where(File.Exists))
        {
            // URLs, names, branch settings, etc. belong to the submodule.
            // Only Git may adjust its path when a parent directory is renamed.
            if (Path.GetFileName(filePath) == ".gitmodules")
            {
                continue;
            }

            await _textRewriter.RewriteAsync(filePath, replacement, cancellationToken);
        }
    }
}
