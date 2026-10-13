using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StickerSnap.Application.Render;
using StickerSnap.Core.Errors;
using StickerSnap.Tests.Application.Exports;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Api;

/// <summary>
/// Previews and exports through the whole server, with a render engine that writes a few bytes
/// instead of running FFmpeg: the files in the stickers folder, Project.exports and when they go
/// stale, GET /api/v1/files, a partial export, and what web mode leaves out.
/// </summary>
public sealed class ExportsEndpointTests : IAsyncDisposable
{
    private const string CatId = "6f1c2a4e-8b3d-4f7a-9c21-5d0e7a3b9f12";

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-exports-");
    private readonly ISegmentRenderService _engine = Substitute.For<ISegmentRenderService>();
    private ApiFactory _factory = null!;
    private WebApplicationFactory<StickerSnap.Api.Program> _server = null!;
    private HttpClient _client = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string Stickers => Path.Combine(_files.FullName, "Стикеры");

    public ExportsEndpointTests()
    {
        _engine.RenderAsync(Arg.Any<SegmentRender>(), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var request = call.Arg<SegmentRender>();
                Renders.ReportStages(call.Arg<IProgress<RenderProgress>?>());
                await File.WriteAllTextAsync(request.OutputPath, $"{request.SegmentId} {request.Quality}", call.Arg<CancellationToken>());
                return Renders.Of(request);
            });
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _factory.DisposeAsync();
        _files.Delete(recursive: true);
    }

    /// <summary>The sample project open, its video in place, and three segments saved at revision 13.</summary>
    private async Task StartAsync(string mode = "desktop")
    {
        _factory = new ApiFactory(("Mode", mode));
        _server = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
            .RemoveAll<ISegmentRenderService>()
            .AddSingleton(_engine)));
        _client = _server.CreateClient();

        var project = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!;
        var source = Path.Combine(_files.FullName, "cat-keyboard.gif");
        await File.WriteAllBytesAsync(source, [0x47, 0x49, 0x46], Cancel);
        project["source"]!["path"] = source;
        var path = Path.Combine(_files.FullName, "cat.ssproj");
        await File.WriteAllTextAsync(path, project.ToJsonString(), Cancel);
        (await _client.PostAsync("/api/v1/projects/open", Body(new JsonObject { ["path"] = path }), Cancel))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PatchAsync("/api/v1/settings", Body(new JsonObject { ["folders"] = new JsonObject { ["stickers"] = Stickers } }), Cancel))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var scene = project["scene"]!.DeepClone();
        var third = scene["segments"]![0]!.DeepClone();
        third["id"] = "seg-3";
        third["name"] = "Стикер 3";
        third["outFrame"] = 9;
        scene["segments"]!.AsArray().Add(third);
        await SaveAsync(12, scene);
    }

    private static StringContent Body(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    private async Task<JsonNode> ProjectAsync() => JsonNode.Parse(await _client.GetStringAsync($"/api/v1/projects/{CatId}", Cancel))!;

    private async Task SaveAsync(int revision, JsonNode scene)
    {
        var saved = await _client.PutAsync(
            $"/api/v1/projects/{CatId}/scene", Body(new JsonObject { ["revision"] = revision, ["scene"] = scene.DeepClone() }), Cancel);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Cancel));
    }

    private async Task<JsonElement> RunAsync(JsonObject request, string end)
    {
        await using var updates = await JobUpdates.ConnectAsync(_server.Server);
        var created = await _client.PostAsync("/api/v1/jobs", Body(request), Cancel);
        created.StatusCode.Should().Be(HttpStatusCode.Accepted, await created.Content.ReadAsStringAsync(Cancel));
        return await updates.UntilAsync(end);
    }

    private static JsonObject Export(int revision, params string[] segments) => new()
    {
        ["kind"] = "export",
        ["projectId"] = CatId,
        ["revision"] = revision,
        ["segmentIds"] = new JsonArray([.. segments.Select(id => JsonValue.Create(id))]),
    };

    private static JsonNode Node(JsonElement element) => JsonNode.Parse(element.GetRawText())!;

    [Fact]
    public async Task Exports_three_segments_into_the_stickers_folder_and_the_project_without_a_new_revision()
    {
        await StartAsync();

        var done = Node(await RunAsync(Export(13, "seg-1", "seg-2", "seg-3"), "succeeded"));

        Directory.EnumerateFiles(Stickers).Select(Path.GetFileName).Should().BeEquivalentTo(
            "Кот и клавиатура — Стикер 1.webm", "Кот и клавиатура — Стикер 2.webm", "Кот и клавиатура — Стикер 3.webm");
        var items = done["items"]!.AsArray();
        items.Select(item => item!["state"]!.GetValue<string>()).Should().Equal("done", "done", "done");
        var result = items[0]!["export"]!;
        result["segmentId"]!.GetValue<string>().Should().Be("seg-1");
        result["sceneRevision"]!.GetValue<int>().Should().Be(13);
        result["stale"]!.GetValue<bool>().Should().BeFalse();
        result["path"]!.GetValue<string>().Should().Be(Path.Combine(Stickers, "Кот и клавиатура — Стикер 1.webm"));
        result["fileUrl"]!.GetValue<string>().Should().StartWith("/api/v1/files/");
        result["bytes"]!.GetValue<long>().Should().Be(190_464);
        result["output"]!.ToJsonString().Should().Be("""{"width":512,"height":512,"fps":24,"durationSec":3}""");
        result["crf"]!.GetValue<int>().Should().Be(40);
        result["concessions"]!.ToJsonString().Should().Be("""[{"kind":"fps","from":30,"to":24}]""");
        result["ladder"]!.ToJsonString().Should().Be(
            """[{"label":[],"bytes":300000},{"label":[{"kind":"fps","from":30,"to":24}],"bytes":190464}]""");
        result["validation"]!["fileName"]!.GetValue<string>().Should().Be("Кот и клавиатура — Стикер 1.webm");
        result["validation"]!["verdict"]!.GetValue<string>().Should().Be("pass");

        var project = await ProjectAsync();
        project["revision"]!.GetValue<int>().Should().Be(13);
        var exports = project["exports"]!.AsObject();
        exports.Select(pair => pair.Key).Should().BeEquivalentTo("seg-1", "seg-2", "seg-3");
        exports["seg-1"]!.ToJsonString().Should().Be(result.ToJsonString());
    }

    [Fact]
    public async Task Marks_stale_only_the_segment_edited_after_the_export()
    {
        await StartAsync();
        await RunAsync(Export(13, "seg-1", "seg-2", "seg-3"), "succeeded");

        var scene = (await ProjectAsync())["scene"]!;
        scene["segments"]![1]!["outFrame"] = 15;
        scene["segments"]![2]!["name"] = "Прыжок";
        await SaveAsync(13, scene);

        var exports = (await ProjectAsync())["exports"]!;
        exports["seg-1"]!["stale"]!.GetValue<bool>().Should().BeFalse();
        exports["seg-2"]!["stale"]!.GetValue<bool>().Should().BeTrue();
        exports["seg-3"]!["stale"]!.GetValue<bool>().Should().BeFalse();
        exports["seg-2"]!["sceneRevision"]!.GetValue<int>().Should().Be(13);
    }

    [Fact]
    public async Task Ends_partial_when_one_segment_fails_and_keeps_the_others()
    {
        await StartAsync();
        _engine.RenderAsync(Arg.Is<SegmentRender>(render => render.SegmentId == "seg-2"), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns<RenderedSegment>(_ => throw new AppException(ErrorCode.CannotFit, "The last step is 300000 bytes"));

        var partial = Node(await RunAsync(Export(13, "seg-1", "seg-2", "seg-3"), "partial"));

        partial["items"]!.AsArray().Select(item => item!["state"]!.GetValue<string>()).Should().Equal("done", "failed", "done");
        partial["items"]![1]!["error"]!["code"]!.GetValue<string>().Should().Be("cannot_fit");
        partial["error"]!["code"]!.GetValue<string>().Should().Be("cannot_fit");
        (await ProjectAsync())["exports"]!.AsObject().Select(pair => pair.Key).Should().BeEquivalentTo("seg-1", "seg-3");
        Directory.EnumerateFiles(Stickers).Should().HaveCount(2);
    }

    [Fact]
    public async Task Replaces_a_segment_s_own_last_export()
    {
        await StartAsync();
        var first = Node(await RunAsync(Export(13, "seg-1"), "succeeded"))["items"]![0]!["export"]!;

        var second = Node(await RunAsync(Export(13, "seg-1"), "succeeded"))["items"]![0]!["export"]!;

        second["path"]!.GetValue<string>().Should().Be(first["path"]!.GetValue<string>());
        Directory.EnumerateFiles(Stickers).Should().ContainSingle();
        (await _client.GetAsync(first["fileUrl"]!.GetValue<string>(), Cancel)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync(second["fileUrl"]!.GetValue<string>(), Cancel)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Serves_an_exported_sticker_whole_and_by_range()
    {
        await StartAsync();
        var done = Node(await RunAsync(Export(13, "seg-1"), "succeeded"));
        var url = done["items"]![0]!["export"]!["fileUrl"]!.GetValue<string>();

        var whole = await _client.GetAsync(url, Cancel);
        var part = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new RangeHeaderValue(0, 4) } };
        var ranged = await _client.SendAsync(part, Cancel);

        whole.StatusCode.Should().Be(HttpStatusCode.OK);
        whole.Content.Headers.ContentType!.MediaType.Should().Be("video/webm");
        (await whole.Content.ReadAsStringAsync(Cancel)).Should().Be("seg-1 Final");
        ranged.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await ranged.Content.ReadAsStringAsync(Cancel)).Should().Be("seg-1");
    }

    [Fact]
    public async Task Renders_an_unsaved_segment_as_a_draft_and_serves_it()
    {
        await StartAsync();
        var segment = (await ProjectAsync())["scene"]!["segments"]![0]!;
        segment["outFrame"] = 19;

        var done = Node(await RunAsync(new JsonObject { ["kind"] = "preview", ["projectId"] = CatId, ["segment"] = segment.DeepClone() }, "succeeded"));

        var preview = done["items"]![0]!["preview"]!;
        preview["bytes"]!.GetValue<long>().Should().Be(190_464);
        var file = await _client.GetAsync(preview["fileUrl"]!.GetValue<string>(), Cancel);
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        (await file.Content.ReadAsStringAsync(Cancel)).Should().Be("seg-1 Draft");
        (await ProjectAsync())["exports"]!.AsObject().Should().BeEmpty();
        Directory.EnumerateFiles(Stickers).Should().BeEmpty();
    }

    [Fact]
    public async Task Leaves_the_path_out_in_web_mode()
    {
        await StartAsync("web");

        var done = Node(await RunAsync(Export(13, "seg-1"), "succeeded"));

        done["items"]![0]!["export"]!.AsObject().ContainsKey("path").Should().BeFalse();
        (await ProjectAsync())["exports"]!["seg-1"]!.AsObject().ContainsKey("path").Should().BeFalse();
    }

    [Theory]
    [InlineData("0f8e1d2c3b4a59687766554433221100")]
    [InlineData("not-an-id")]
    public async Task Says_file_not_found_for_a_file_it_does_not_have(string fileId)
    {
        await StartAsync();

        var response = await _client.GetAsync($"/api/v1/files/{fileId}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["code"]!.GetValue<string>().Should().Be("file_not_found");
    }
}
