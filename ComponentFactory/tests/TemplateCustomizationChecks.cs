using System.Text;
using ComponentFactory.Configuration;
using ComponentFactory.Domain;
using ComponentFactory.Infrastructure.Templates;
using Microsoft.Extensions.Options;

namespace FactoryChecks;

internal static class TemplateCustomizationChecks
{
    public static async Task RunAsync()
    {
        using var temporary = new TestDirectory();
        var customizer = CreateCustomizer();

        await CheckNamesAndContentsAsync(temporary, customizer);
        await CheckCollisionAsync(temporary, customizer);
        await CheckUnsupportedFeaturesAsync(temporary, customizer);
        CheckSinglePassReplacement();
    }

    private static TemplateCustomizer CreateCustomizer()
    {
        return new TemplateCustomizer(
            new TemplateScanner(),
            new Utf8TextRewriter(),
            Options.Create(new FactoryOptions { TemplateName = "TemplateSensor" }));
    }

    private static async Task CheckNamesAndContentsAsync(
        TestDirectory temporary,
        TemplateCustomizer customizer)
    {
        var root = temporary.CreateSubdirectory("rename");
        var agentPath = System.IO.Path.Combine(root, "TemplateSensorAgent");
        Directory.CreateDirectory(agentPath);

        await File.WriteAllTextAsync(
            System.IO.Path.Combine(agentPath, "TemplateSensorAgent.csproj"),
            "TemplateSensor templatesensor TEMPLATESENSOR",
            new UTF8Encoding(true));

        await File.WriteAllTextAsync(
            System.IO.Path.Combine(root, ".gitlab-ci.yml"),
            "SENSOR_NAME: \"TemplateSensor\"\r\nAGENT_NAME: \"templatesensor\"\r\n");

        byte[] binary = [0, 1, 2, 84, 101];
        var binaryPath = System.IO.Path.Combine(root, "asset.bin");
        await File.WriteAllBytesAsync(binaryPath, binary);

        var utf16Path = System.IO.Path.Combine(root, "utf16.txt");
        await File.WriteAllTextAsync(utf16Path, "TemplateSensor", Encoding.Unicode);
        var utf16Before = await File.ReadAllBytesAsync(utf16Path);

        await customizer.CustomizeAsync(root, ComponentName.Create("Bravo"), CancellationToken.None);

        var renamedFile = System.IO.Path.Combine(root, "BravoAgent", "BravoAgent.csproj");
        Assert.True(File.Exists(renamedFile), "nested directory and filename replacement");
        Assert.True(await File.ReadAllTextAsync(renamedFile) == "Bravo bravo BRAVO", "three case variants");

        var renamedBytes = await File.ReadAllBytesAsync(renamedFile);
        Assert.True(renamedBytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "UTF-8 BOM preserved");

        var ciContent = await File.ReadAllTextAsync(System.IO.Path.Combine(root, ".gitlab-ci.yml"));
        Assert.True(ciContent == "SENSOR_NAME: \"Bravo\"\r\nAGENT_NAME: \"bravo\"\r\n", "CI variables and CRLF preserved");
        Assert.True((await File.ReadAllBytesAsync(binaryPath)).SequenceEqual(binary), "binary file preserved");
        Assert.True((await File.ReadAllBytesAsync(utf16Path)).SequenceEqual(utf16Before), "non-UTF-8 file preserved");
    }

    private static async Task CheckCollisionAsync(TestDirectory temporary, TemplateCustomizer customizer)
    {
        var root = temporary.CreateSubdirectory("collision");
        var source = System.IO.Path.Combine(root, "TemplateSensor.txt");

        await File.WriteAllTextAsync(source, "TemplateSensor");
        await File.WriteAllTextAsync(System.IO.Path.Combine(root, "Bravo.txt"), "existing");

        var exception = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            customizer.CustomizeAsync(root, ComponentName.Create("Bravo"), CancellationToken.None));

        Assert.True(exception.Error == FactoryError.InvalidTemplate, "colliding names rejected");
        Assert.True(await File.ReadAllTextAsync(source) == "TemplateSensor", "collision checked before content mutation");
    }

    private static async Task CheckUnsupportedFeaturesAsync(TestDirectory temporary, TemplateCustomizer customizer)
    {
        var submodules = temporary.CreateSubdirectory("submodules");
        await File.WriteAllTextAsync(System.IO.Path.Combine(submodules, ".gitmodules"), "");

        var submoduleError = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            customizer.CustomizeAsync(submodules, ComponentName.Create("Bravo"), CancellationToken.None));

        Assert.True(submoduleError.Error == FactoryError.InvalidTemplate, "submodules rejected");

        var lfs = temporary.CreateSubdirectory("lfs");
        await File.WriteAllTextAsync(System.IO.Path.Combine(lfs, ".gitattributes"), "*.bin filter=lfs diff=lfs merge=lfs -text");

        var lfsError = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            customizer.CustomizeAsync(lfs, ComponentName.Create("Bravo"), CancellationToken.None));

        Assert.True(lfsError.Error == FactoryError.InvalidTemplate, "Git LFS rejected");

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var links = temporary.CreateSubdirectory("links");
        File.CreateSymbolicLink(System.IO.Path.Combine(links, "link"), submodules);

        var linkError = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            customizer.CustomizeAsync(links, ComponentName.Create("Bravo"), CancellationToken.None));

        Assert.True(linkError.Error == FactoryError.InvalidTemplate, "symbolic links rejected");
    }

    private static void CheckSinglePassReplacement()
    {
        var replacement = new NameReplacement("TemplateSensor", ComponentName.Create("TemplateSensorBravo"));

        Assert.True(
            replacement.Apply("TemplateSensor templatesensor TEMPLATESENSOR")
                == "TemplateSensorBravo templatesensorbravo TEMPLATESENSORBRAVO",
            "inserted names are not replaced again");
    }
}
