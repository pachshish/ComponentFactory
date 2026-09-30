using ComponentFactory.Application.Abstractions;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Infrastructure.Templates;

public sealed class TemplateCustomizer(
    TemplateScanner scanner,
    Utf8TextRewriter textRewriter,
    IOptions<FactoryOptions> options) : ITemplateCustomizer
{
    private readonly TemplateScanner _scanner = scanner;
    private readonly Utf8TextRewriter _textRewriter = textRewriter;
    private readonly string _templateName = options.Value.TemplateName;

    public async Task CustomizeAsync(
        string repositoryPath,
        ComponentName name,
        CancellationToken cancellationToken)
    {
        var entries = _scanner.Scan(repositoryPath, cancellationToken);
        var replacement = new NameReplacement(_templateName, name);
        var renamePlan = RenamePlan.Create(repositoryPath, entries, replacement);

        await RewriteFileContentsAsync(entries, replacement, cancellationToken);

        renamePlan.Apply(cancellationToken);
    }

    private async Task RewriteFileContentsAsync(
        IEnumerable<string> entries,
        NameReplacement replacement,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in entries.Where(File.Exists))
        {
            await _textRewriter.RewriteAsync(filePath, replacement, cancellationToken);
        }
    }
}
