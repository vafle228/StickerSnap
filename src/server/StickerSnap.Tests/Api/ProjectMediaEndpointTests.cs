using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StickerSnap.Application.Media;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Api;

/// <summary>
/// GET cover, media and frames through the whole server, with a media engine that writes a line
/// of text instead of running FFmpeg: content types, Range, ETags, the cache, and the refusals.
/// </summary>
public sealed class ProjectMediaEndpointTests : IAsyncDisposable
{
    private const string CatId = "6f1c2a4e-8b3d-4f7a-9c21-5d0e7a3b9f12";

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-media-api-");
    private readonly IEditorMedia _media = Substitute.For<IEditorMedia>();
    private readonly ApiFactory _factory = new();
    private WebApplicationFactory<StickerSnap.Api.Program> _server = null!;
    private HttpClient _client = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string Source => Path.Combine(_files.FullName, "cat-keyboard.gif");

    public ProjectMediaEndpointTests()
    {
        _media.WriteStripAsync(Arg.Any<string>(), Arg.Any<MediaInfo>(), Arg.Any<FrameStrip>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => File.WriteAllTextAsync(call.ArgAt<string>(3), $"strip {call.Arg<FrameStrip>().From}+{call.Arg<FrameStrip>().Count}"));
        _media.WriteCoverAsync(Arg.Any<string>(), Arg.Any<MediaInfo>(), Arg.Any<CoverShot>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => File.WriteAllTextAsync(call.ArgAt<string>(3), $"cover {call.Arg<CoverShot>().Frame}"));
        _media.WritePlayableAsync(Arg.Any<string>(), Arg.Any<MediaInfo>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => File.WriteAllTextAsync(call.ArgAt<string>(2), "a playable copy"));
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _factory.DisposeAsync();
        _files.Delete(recursive: true);
    }

    /// <summary>The sample project open, its source the GIF or <paramref name="change"/> of it, on disk unless told otherwise.</summary>
    private async Task StartAsync(Action<JsonNode>? change = null, bool withSource = true)
    {
        _server = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
            .RemoveAll<IEditorMedia>()
            .AddSingleton(_media)));
        _client = _server.CreateClient();

        var project = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!;
        if (withSource) await File.WriteAllTextAsync(Source, "the source itself", Cancel);
        project["source"]!["path"] = Source;
        change?.Invoke(project);
        var path = Path.Combine(_files.FullName, "cat.ssproj");
        await File.WriteAllTextAsync(path, project.ToJsonString(), Cancel);
        var opened = await _client.PostAsync(
            "/api/v1/projects/open",
            new StringContent(new JsonObject { ["path"] = path }.ToJsonString(), Encoding.UTF8, "application/json"),
            Cancel);
        opened.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static void AsMp4(JsonNode project)
    {
        project["source"]!["media"]!["format"] = "mp4";
        project["source"]!["media"]!["codec"] = "h264";
    }

    private async Task<string> MediaUrlAsync() =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/v1/projects/{CatId}", Cancel))!["mediaUrl"]!.GetValue<string>();

    private static async Task<JsonNode> ProblemAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    [Fact]
    public async Task Plays_a_gif_as_a_webm_copy_made_once()
    {
        await StartAsync();

        var first = await _client.GetAsync(await MediaUrlAsync(), Cancel);
        var second = await _client.GetAsync($"/api/v1/projects/{CatId}/media", Cancel);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.Should().Be("video/webm");
        (await first.Content.ReadAsStringAsync(Cancel)).Should().Be("a playable copy");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        await _media.Received(1).WritePlayableAsync(Source, Arg.Any<MediaInfo>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Answers_part_of_the_video_for_a_range()
    {
        await StartAsync(AsMp4);
        using var request = new HttpRequestMessage(HttpMethod.Get, await MediaUrlAsync());
        request.Headers.Range = new RangeHeaderValue(4, 9);

        var response = await _client.SendAsync(request, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentType!.MediaType.Should().Be("video/mp4");
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes 4-9/17");
        (await response.Content.ReadAsStringAsync(Cancel)).Should().Be("source");
        await _media.DidNotReceive().WritePlayableAsync(Arg.Any<string>(), Arg.Any<MediaInfo>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Says_not_modified_to_a_browser_that_has_the_same_content()
    {
        await StartAsync(AsMp4);
        var first = await _client.GetAsync($"/api/v1/projects/{CatId}/media", Cancel);
        first.Headers.CacheControl!.Private.Should().BeTrue();
        first.Headers.CacheControl.NoCache.Should().BeTrue();
        var tag = first.Headers.ETag!;
        using var again = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/projects/{CatId}/media");
        again.Headers.IfNoneMatch.Add(tag);

        var response = await _client.SendAsync(again, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Gives_the_frames_of_the_query_as_webp_and_keeps_them()
    {
        await StartAsync();

        var first = await _client.GetAsync($"/api/v1/projects/{CatId}/frames?from=0&count=30&height=96", Cancel);
        var second = await _client.GetAsync($"/api/v1/projects/{CatId}/frames?from=0&count=30&height=96", Cancel);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        (await first.Content.ReadAsStringAsync(Cancel)).Should().Be("strip 0+30");
        second.Headers.ETag.Should().Be(first.Headers.ETag);
        await _media.Received(1).WriteStripAsync(
            Arg.Any<string>(), Arg.Any<MediaInfo>(), Arg.Any<FrameStrip>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("from=30&count=30&height=96", "count", "out_of_range")]
    [InlineData("from=0&count=30&height=721", "height", "out_of_range")]
    public async Task Refuses_frames_out_of_range_naming_the_field(string query, string field, string reason)
    {
        await StartAsync();

        var response = await _client.GetAsync($"/api/v1/projects/{CatId}/frames?{query}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ProblemAsync(response);
        problem["code"]!.GetValue<string>().Should().Be("bad_request");
        problem["fields"]![field]![0]!.GetValue<string>().Should().Be(reason);
    }

    [Fact]
    public async Task Refuses_frames_without_a_height()
    {
        await StartAsync();

        var response = await _client.GetAsync($"/api/v1/projects/{CatId}/frames?from=0&count=30", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response))["code"]!.GetValue<string>().Should().Be("bad_request");
    }

    [Fact]
    public async Task Gives_the_cover_of_the_first_sticker()
    {
        await StartAsync();

        var response = await _client.GetAsync($"/api/v1/projects/{CatId}/cover", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        (await response.Content.ReadAsStringAsync(Cancel)).Should().Be("cover 14");
    }

    [Fact]
    public async Task Says_the_video_is_gone_for_the_media_and_the_frames()
    {
        await StartAsync(withSource: false);

        var media = await _client.GetAsync($"/api/v1/projects/{CatId}/media", Cancel);
        var frames = await _client.GetAsync($"/api/v1/projects/{CatId}/frames?from=0&count=30&height=96", Cancel);
        var cover = await _client.GetAsync($"/api/v1/projects/{CatId}/cover", Cancel);

        foreach (var response in new[] { media, frames, cover })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ProblemAsync(response))["code"]!.GetValue<string>().Should().Be("source_not_found");
        }
    }

    [Fact]
    public async Task Says_an_unknown_project_is_not_found()
    {
        await StartAsync();

        var response = await _client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/cover", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemAsync(response))["code"]!.GetValue<string>().Should().Be("project_not_found");
    }
}
