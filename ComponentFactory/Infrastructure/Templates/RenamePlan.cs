using ComponentFactory.Domain;

namespace ComponentFactory.Infrastructure.Templates;

public sealed class RenamePlan
{
    private readonly IReadOnlyList<PathRename> _operations;

    private RenamePlan(IReadOnlyList<PathRename> operations)
    {
        _operations = operations;
    }

    public static RenamePlan Create(
        string rootPath,
        IReadOnlyList<string> entries,
        NameReplacement replacement)
    {
        EnsureNoCollisions(rootPath, entries, replacement);

        var operations = entries
            .OrderByDescending(GetDepth)
            .ThenByDescending(path => path.Length)
            .Select(path => CreateOperation(path, replacement))
            .Where(operation => operation.Source != operation.Target)
            .ToArray();

        return new RenamePlan(operations);
    }

    public void Apply(CancellationToken cancellationToken)
    {
        // Rename children before parents, so original child paths remain accessible.
        foreach (var operation in _operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (operation.IsDirectory)
            {
                Directory.Move(operation.Source, operation.Target);
            }
            else
            {
                File.Move(operation.Source, operation.Target);
            }
        }
    }

    private static void EnsureNoCollisions(
        string rootPath,
        IReadOnlyList<string> entries,
        NameReplacement replacement)
    {
        var targets = entries
            .Select(path => Path.GetRelativePath(rootPath, path))
            .Select(replacement.Apply)
            .ToArray();

        if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
        {
            throw new ComponentFactoryException(
                FactoryError.InvalidTemplate,
                "Renaming would create colliding paths.");
        }
    }

    private static PathRename CreateOperation(string source, NameReplacement replacement)
    {
        var newName = replacement.Apply(Path.GetFileName(source));
        var target = Path.Combine(Path.GetDirectoryName(source)!, newName);

        return new PathRename(source, target, Directory.Exists(source));
    }

    private static int GetDepth(string path)
    {
        return path.Count(character => character == Path.DirectorySeparatorChar);
    }

    private sealed record PathRename(string Source, string Target, bool IsDirectory);
}
