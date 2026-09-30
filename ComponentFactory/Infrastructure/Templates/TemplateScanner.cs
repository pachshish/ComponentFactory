using ComponentFactory.Domain;

namespace ComponentFactory.Infrastructure.Templates;

public sealed class TemplateScanner
{
    public IReadOnlyList<string> Scan(string rootPath, CancellationToken cancellationToken)
    {
        var entries = new List<string>();

        CollectEntries(rootPath, entries, cancellationToken);
        RejectUnsupportedFeatures(entries);

        return entries;
    }

    private static void CollectEntries(
        string directoryPath,
        List<string> entries,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectSymbolicLink(entry);

            entries.Add(entry);

            if (Directory.Exists(entry))
            {
                CollectEntries(entry, entries, cancellationToken);
            }
        }
    }

    private static void RejectSymbolicLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw InvalidTemplate("Template must not contain symbolic links.");
        }
    }

    private static void RejectUnsupportedFeatures(IEnumerable<string> entries)
    {
        foreach (var file in entries.Where(File.Exists))
        {
            var fileName = Path.GetFileName(file);

            if (fileName == ".gitmodules")
            {
                throw InvalidTemplate("Submodule templates are not supported.");
            }

            if (fileName == ".gitattributes" && File.ReadAllText(file).Contains("filter=lfs", StringComparison.Ordinal))
            {
                throw InvalidTemplate("Git LFS templates are not supported.");
            }
        }
    }

    private static ComponentFactoryException InvalidTemplate(string message)
    {
        return new ComponentFactoryException(FactoryError.InvalidTemplate, message);
    }
}
