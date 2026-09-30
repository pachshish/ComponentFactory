using System.Text.RegularExpressions;
using ComponentFactory.Domain;

namespace ComponentFactory.Infrastructure.Templates;

public sealed class NameReplacement
{
    private readonly IReadOnlyDictionary<string, string> _replacements;
    private readonly Regex _pattern;

    public NameReplacement(string templateName, ComponentName componentName)
    {
        _replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [templateName] = componentName.Value,
            [templateName.ToLowerInvariant()] = componentName.ProjectPath,
            [templateName.ToUpperInvariant()] = componentName.UpperCase
        };

        var alternatives = _replacements.Keys
            .OrderByDescending(value => value.Length)
            .Select(Regex.Escape);

        _pattern = new Regex(string.Join("|", alternatives), RegexOptions.CultureInvariant);
    }

    public string Apply(string value)
    {
        // One pass prevents replacements from rewriting text they just inserted.
        return _pattern.Replace(value, match => _replacements[match.Value]);
    }
}
