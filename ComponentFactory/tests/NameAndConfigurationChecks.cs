using ComponentFactory.Configuration;
using ComponentFactory.Domain;

namespace FactoryChecks;

internal static class NameAndConfigurationChecks
{
    public static void Run()
    {
        var name = ComponentName.Create("Bravo");

        Assert.True(name.ProjectPath == "bravo" && name.UpperCase == "BRAVO", "component name exposes case variants");

        foreach (var invalidName in new string?[] { null, "", "bravo", "../Bravo", "Bravo\n", "A", new string('A', 65) })
        {
            CheckRejectedName(invalidName);
        }

        var validator = new FactoryOptionsValidator();
        var options = new FactoryOptions
        {
            GitLabUrl = "https://gitlab.example",
            Token = "token",
            ApiKey = "key",
            NamespaceId = 456,
            TemplateProjectId = 123
        };

        Assert.True(validator.Validate(null, options).Succeeded, "valid configuration accepted");

        options.TemplateName = "ALFA";
        Assert.True(validator.Validate(null, options).Failed, "ambiguous template case variants rejected");
    }

    private static void CheckRejectedName(string? name)
    {
        try
        {
            ComponentName.Create(name);
        }
        catch (ComponentFactoryException exception)
        {
            Assert.True(exception.Error == FactoryError.InvalidName, $"invalid name rejected: {name ?? "null"}");
            return;
        }

        throw new InvalidOperationException("Invalid component name was accepted.");
    }
}
