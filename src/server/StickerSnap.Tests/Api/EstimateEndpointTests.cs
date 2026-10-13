using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StickerSnap.Application.Media;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Api;

/// <summary>
/// POST …/estimate through the whole server, with a media engine that names a size per CRF
/// instead of running FFmpeg: the JSON the inspector reads, and the refusals.
/// </summary>
public sealed class EstimateEndpointTests : IAsyncDisposable
{
    private const string CatId = "6f1c2a4e-8b3d-4f7a-9c21-5d0e7a3b9f12";

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-estimate-api-");
    private readonly IStickerRenderer _renderer = Substitute.For<IStickerRenderer>();
    private readonly ApiFactory _factory = new();
    private WebApplicationFactory<StickerSnap.Api.Program> _server = null!;
    private HttpClient _client = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public EstimateEndpointTests()
    {
        _renderer.EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(BytesOf(call.Arg<StickerProbe>())));
    }

    /// <summary>10 000 B a frame played at CRF 40, half that higher up.</summary>
    private static long BytesOf(StickerProbe probe) => probe.Spec.PlayedFrames * (probe.Crf == 40 ? 10_000L : 5_000L);

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _factory.DisposeAsync();
        _files.Delete(recursive: true);
    }

    /// <summary>The sample project open, its GIF on disk unless told otherwise.</summary>
    private async Task StartAsync(bool withSource = true)
    {
        _server = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
            .RemoveAll<IStickerRenderer>()
            .AddSingleton(_renderer)));
        _client = _server.CreateClient();

        var source = Path.Combine(_files.FullName, "cat-keyboard.gif");
        if (withSource) await File.WriteAllTextAsync(source, "the source itself", Cancel);
        var project = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!;
        project["source"]!["path"] = source;
        var path = Path.Combine(_files.FullName, "cat.ssproj");
        await File.WriteAllTextAsync(path, project.ToJsonString(), Cancel);
        var opened = await _client.PostAsync("/api/v1/projects/open", Json(new JsonObject { ["path"] = path }), Cancel);
        opened.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>The first segment of the sample, changed by <paramref name="change"/>, as the SPA sends it.</summary>
    private async Task<HttpResponseMessage> EstimateAsync(Action<JsonNode>? change = null, string id = CatId)
    {
        var segment = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!["scene"]!["segments"]![0]!.DeepClone();
        change?.Invoke(segment);
        return await _client.PostAsync($"/api/v1/projects/{id}/estimate", Json(new JsonObject { ["segment"] = segment }), Cancel);
    }

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    [Fact]
    public async Task Tells_the_size_the_output_what_will_be_given_up_and_the_fixes()
    {
        await StartAsync();

        var response = await EstimateAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var expected = JsonNode.Parse("""
            {
              "bytes": 300000,
              "limitBytes": 262144,
              "fits": false,
              "output": { "width": 512, "height": 512, "fps": 10, "durationSec": 3 },
              "likelyConcessions": [ { "kind": "crf", "from": 40, "to": 46 } ],
              "suggestions": [ { "kind": "trim", "outFrame": 23, "durationSec": 2.4 }, { "kind": "speed", "speed": 3.7 } ]
            }
            """);
        JsonNode.DeepEquals(await BodyAsync(response), expected).Should().BeTrue(await response.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task Gives_nothing_up_for_a_sticker_that_fits()
    {
        await StartAsync();

        var body = await BodyAsync(await EstimateAsync(segment => segment["outFrame"] = 9));

        body["fits"]!.GetValue<bool>().Should().BeTrue();
        body["likelyConcessions"]!.AsArray().Should().BeEmpty();
        body["suggestions"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_a_segment_past_the_end_of_the_video_naming_the_field()
    {
        await StartAsync();

        var response = await EstimateAsync(segment => segment["outFrame"] = 99);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await BodyAsync(response);
        problem["code"]!.GetValue<string>().Should().Be("bad_request");
        problem["fields"]!["segment.outFrame"]![0]!.GetValue<string>().Should().Be("out_of_range");
    }

    [Fact]
    public async Task Says_an_unknown_project_is_not_found()
    {
        await StartAsync();

        var response = await EstimateAsync(id: "00000000-0000-4000-8000-000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyAsync(response))["code"]!.GetValue<string>().Should().Be("project_not_found");
    }

    [Fact]
    public async Task Says_the_video_is_gone()
    {
        await StartAsync(withSource: false);

        var response = await EstimateAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyAsync(response))["code"]!.GetValue<string>().Should().Be("source_not_found");
    }
}
