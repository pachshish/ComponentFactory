namespace ComponentFactory.Application.Abstractions;

public interface IWorkspace : IDisposable
{
    string RootPath { get; }

    string RepositoryPath { get; }

    string AskPassPath { get; }
}
