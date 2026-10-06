using ComponentFactory.Api;
using ComponentFactory.Application;
using ComponentFactory.Application.Abstractions;
using ComponentFactory.Infrastructure.Cd;
using ComponentFactory.Infrastructure.Git;
using ComponentFactory.Infrastructure.GitLab;
using ComponentFactory.Infrastructure.Templates;
using ComponentFactory.Infrastructure.Workspaces;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComponentFactory(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddExceptionHandler<FactoryExceptionHandler>();
        services.AddProblemDetails();
        services.AddScoped<ApiKeyAuthorizationFilter>();

        AddConfiguration(services);
        AddGitLabClient(services);
        AddGenerationServices(services);

        return services;
    }

    private static void AddConfiguration(IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<FactoryOptions>, FactoryOptionsValidator>();

        services.AddOptions<FactoryOptions>()
            .BindConfiguration(FactoryOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<CdOptions>, CdOptionsValidator>();
        services.AddOptions<CdOptions>()
            .BindConfiguration(CdOptions.SectionName)
            .ValidateOnStart();
    }

    private static void AddGitLabClient(IServiceCollection services)
    {
        services.AddHttpClient<IGitLabClient, GitLabClient>(ConfigureGitLabClient)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
    }

    private static void ConfigureGitLabClient(IServiceProvider provider, HttpClient client)
    {
        var options = provider.GetRequiredService<IOptions<FactoryOptions>>().Value;

        client.BaseAddress = new Uri(options.GitLabUrl.TrimEnd('/') + "/api/v4/");
        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", options.Token);
        client.Timeout = TimeSpan.FromSeconds(60);
    }

    private static void AddGenerationServices(IServiceCollection services)
    {
        services.AddTransient<IComponentGenerator, ComponentGenerator>();
        services.AddTransient<ICdProvisioner, CdProvisioner>();
        services.AddTransient<IGitRepository, GitRepository>();
        services.AddTransient<ITemplateCustomizer, TemplateCustomizer>();
        services.AddTransient<IWorkspaceFactory, WorkspaceFactory>();

        services.AddTransient<GitCommandRunner>();
        services.AddSingleton<RepositoryUrlValidator>();
        services.AddSingleton<TemplateScanner>();
        services.AddSingleton<Utf8TextRewriter>();
    }
}
