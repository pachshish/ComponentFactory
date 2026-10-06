namespace ComponentFactory.Configuration;

public sealed class CdOptions
{
    public const string SectionName = "Cd";

    public long ProjectId { get; set; }

    public string Branch { get; set; } = "master";

    public string TemplatePath { get; set; } = "sensorgates/senortemplate";

    public string TargetRoot { get; set; } = "sensorgates";

    public string TemplateName { get; set; } = "SenorTemplate";
}
