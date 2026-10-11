using System.Net;

namespace StickerSnap.Tests.Api;

/// <summary>The server serves the SPA, and an unknown API or hub path never gets the SPA page.</summary>
public sealed class SpaHostTests(SpaHostTests.Server server) : IClassFixture<SpaHostTests.Server>
{
    /// <summary>One server for the class, as run by hand or in the browser: no shell, no token.</summary>
    public sealed class Server() : ApiFactory;

    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/nothing-here")]
    [InlineData("/api/v1/projects/6f1d2c3a/unknown")]
    public async Task Answers_bad_request_for_an_api_path_without_a_handler(string path)
    {
        var response = await _client.GetAsync(path, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Answers_404_for_a_hub_path_without_a_handler()
    {
        var response = await _client.GetAsync("/hubs/nothing-here", Cancel);

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
