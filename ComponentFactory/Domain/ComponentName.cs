using System.Text.RegularExpressions;

namespace ComponentFactory.Domain;

public sealed record ComponentName
{
    private static readonly Regex ValidName = new(
        "\\A[A-Z][A-Za-z0-9]{1,63}\\z",
        RegexOptions.CultureInvariant);

    private ComponentName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public string ProjectPath => Value.ToLowerInvariant();

    public string UpperCase => Value.ToUpperInvariant();

    public static ComponentName Create(string? value)
    {
        if (string.IsNullOrEmpty(value) || !ValidName.IsMatch(value))
        {
            throw new ComponentFactoryException(
                FactoryError.InvalidName,
                "ComponentName must contain 2–64 ASCII letters/digits and start with an uppercase letter. Example: Bravo.");
        }

        return new ComponentName(value);
    }
}
