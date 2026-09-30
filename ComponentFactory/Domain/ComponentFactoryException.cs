namespace ComponentFactory.Domain;

public enum FactoryError
{
    InvalidName,
    InvalidTemplate,
    ProjectAlreadyExists,
    ExternalOperationFailed,
    GenerationIncomplete
}

public sealed class ComponentFactoryException(
    FactoryError error,
    string message,
    string? projectUrl = null,
    string? stage = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public FactoryError Error { get; } = error;

    public string? ProjectUrl { get; } = projectUrl;

    public string? Stage { get; } = stage;
}
