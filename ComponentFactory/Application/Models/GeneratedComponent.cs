namespace ComponentFactory.Application.Models;

public sealed record GeneratedComponent(
    long ProjectId,
    string WebUrl,
    string ComponentName,
    string Branch);
