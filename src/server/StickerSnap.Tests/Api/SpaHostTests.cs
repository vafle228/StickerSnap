using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StickerSnap.Tests;

/// <summary>The server serves the SPA, and an unknown API path never gets the SPA page.</summary>
public sealed class SpaHostTests : IClassFixture<SpaHostTests.Factory>
{
    private const string Page = "<!doctype html><div id=\"root\"></div>";

    /// <summary>The app with its own wwwroot, so the test does not need `npm run build`.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _webRoot = Directory.CreateTempSubdirectory("stickersnap-wwwroot-").FullName;

        public Factory() => File.WriteAllText(Path.Combine(_webRoot, "index.html"), Page);

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseWebRoot(_webRoot);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    private readonly HttpClient _client;

    public SpaHostTests(Factory factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/v1/nothing-here")]
    [InlineData("/api/v1/projects/6f1d2c3a/unknown")]
    [InlineData("/hubs/nothing-here")]
    public async Task Answers_404_for_an_api_or_hub_path_without_a_handler(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/projects")]
    [InlineData("/settings/look")]
    public async Task Serves_the_spa_page_for_every_other_path(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Page, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
