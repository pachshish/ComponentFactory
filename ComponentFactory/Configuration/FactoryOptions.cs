namespace ComponentFactory.Configuration;

public sealed class FactoryOptions
{
    public const string SectionName = "Factory";

    public string GitLabUrl { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public long TemplateProjectId { get; set; }

    public long NamespaceId { get; set; }

    public string TemplateName { get; set; } = "TemplateSensor";

    public string WorkRoot { get; set; } = "/tmp/component-factory";

    public int OperationTimeoutSeconds { get; set; } = 300;
}
