using System.Net;

namespace StickerSnap.Tests.Api;

/// <summary>The server serves the SPA, and an unknown API path never gets the SPA page.</summary>
public sealed class SpaHostTests(SpaHostTests.Server server) : IClassFixture<SpaHostTests.Server>
{
    /// <summary>One server for the class, as run by hand or in the browser: no shell, no token.</summary>
    public sealed class Server() : ApiFactory;

    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/nothing-here")]
    [InlineData("/api/v1/projects/6f1d2c3a/unknown")]
    [InlineData("/hubs/nothing-here")]
    public async Task Answers_404_for_an_api_or_hub_path_without_a_handler(string path)
    {
        var response = await _client.GetAsync(path, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/projects")]
    [InlineData("/settings/look")]
    public async Task Serves_the_spa_page_for_every_other_path(string path)
    {
        var response = await _client.GetAsync(path, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(Cancel)).Should().Be(ApiFactory.Page);
    }
}
