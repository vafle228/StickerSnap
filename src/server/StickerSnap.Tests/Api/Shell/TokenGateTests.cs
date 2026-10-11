using System.Net;
using System.Net.Http.Headers;

namespace StickerSnap.Tests.Api.Shell;

/// <summary>With StickerSnap__Token set by the shell, /api and /hubs let in only that token.</summary>
public sealed class TokenGateTests
{
    private const string Token = "launch-token";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/app")]
    [InlineData("/api/v1/projects/6f1d2c3a")]
    [InlineData("/hubs/events/negotiate")]
    public async Task Answers_401_to_a_request_without_the_launch_token(string path)
    {
        using var factory = new ApiFactory(("Token", Token));
        var client = factory.CreateClient();

        var response = await client.GetAsync(path, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
    }

    [Fact]
    public async Task Answers_401_to_a_wrong_token()
    {
        using var factory = new ApiFactory(("Token", Token));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "guess");

        var byHeader = await client.GetAsync("/api/v1/app", Cancel);
        var byQuery = await client.GetAsync("/api/v1/app?access_token=guess", Cancel);

        byHeader.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        byQuery.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lets_the_token_in_as_a_bearer_header()
    {
        using var factory = new ApiFactory(("Token", Token));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await client.GetAsync("/api/v1/nothing-here", Cancel);

        // Past the gate: no endpoint yet, so the API's own 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/v1/projects/6f1d2c3a/media")]
    [InlineData("/hubs/events/negotiate")]
    public async Task Lets_the_token_in_as_access_token_where_the_browser_loads_by_itself(string path)
    {
        using var factory = new ApiFactory(("Token", Token));
        var client = factory.CreateClient();

        var response = await client.GetAsync($"{path}?access_token={Token}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Serves_the_spa_page_without_the_token()
    {
        using var factory = new ApiFactory(("Token", Token));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Keeps_the_api_open_when_no_shell_passed_a_token()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/nothing-here", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
