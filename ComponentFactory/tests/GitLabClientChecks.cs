using System.Net;
using System.Text;
using System.Text.Json;
using ComponentFactory.Domain;
using ComponentFactory.Infrastructure.GitLab;

namespace FactoryChecks;

internal static class GitLabClientChecks
{
    public static async Task RunAsync()
    {
        using var handler = new FakeGitLabHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gitlab.example/api/v4/")
        };

        var client = new GitLabClient(httpClient);

        await client.EnsureProjectNameAvailableAsync(456, "bravo", CancellationToken.None);
        var created = await client.CreateProjectAsync(456, "bravo", CancellationToken.None);
        using var body = JsonDocument.Parse(handler.CreateBody);

        Assert.True(created.Id == 9, "GitLab project response mapped");
        Assert.True(body.RootElement.GetProperty("namespace_id").GetInt64() == 456, "fixed target namespace");
        Assert.True(body.RootElement.GetProperty("path").GetString() == "bravo", "lowercase project path");
        Assert.True(!body.RootElement.GetProperty("initialize_with_readme").GetBoolean(), "empty independent project creation");

        handler.Exists = true;

        var existingError = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            client.EnsureProjectNameAvailableAsync(456, "bravo", CancellationToken.None));

        Assert.True(existingError.Error == FactoryError.ProjectAlreadyExists, "existing project rejected");

        var raceError = await Assert.ThrowsAsync<ComponentFactoryException>(() =>
            client.CreateProjectAsync(456, "bravo", CancellationToken.None));

        Assert.True(raceError.Error == FactoryError.ProjectAlreadyExists, "concurrent creation conflict identified");
    }

    private sealed class FakeGitLabHandler : HttpMessageHandler
    {
        public bool Exists { get; set; }

        public string CreateBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                CreateBody = await request.Content!.ReadAsStringAsync(cancellationToken);

                return JsonResponse(
                    Exists ? HttpStatusCode.BadRequest : HttpStatusCode.Created,
                    """
                    {
                        "id": 9,
                        "web_url": "https://gitlab.example/team/bravo",
                        "http_url_to_repo": "https://gitlab.example/team/bravo.git"
                    }
                    """);
            }

            if (request.RequestUri!.AbsolutePath.Contains("/groups/", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, """{"full_path":"team/components"}""");
            }

            return new HttpResponseMessage(Exists ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
