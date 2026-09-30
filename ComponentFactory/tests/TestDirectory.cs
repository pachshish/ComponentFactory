namespace FactoryChecks;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "factory-checks-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateSubdirectory(string name)
    {
        var directory = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(directory);

        return directory;
    }

    public void Dispose()
    {
        Directory.Delete(Path, recursive: true);
    }
}
