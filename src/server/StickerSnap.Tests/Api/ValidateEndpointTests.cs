using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using Samples = StickerSnap.Tests.Core.Render.Media;

namespace StickerSnap.Tests.Api;

/// <summary>POST /api/v1/validate over a substituted probe: the report's shape and the errors' codes.</summary>
public sealed class ValidateEndpointTests : IDisposable
{
    private readonly ApiFactory _desktop = new(("Mode", "desktop"));
    private readonly IMediaProbe _probe = Substitute.For<IMediaProbe>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _desktop.Dispose();

    private Task<HttpResponseMessage> Post(string body, ApiFactory? factory = null) =>
        (factory ?? _desktop)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(_probe)))
            .CreateClient()
            .PostAsync("/api/v1/validate", new StringContent(body, Encoding.UTF8, "application/json"), Cancel);

    private static string PathBody(string path) =>
        new JsonObject { ["source"] = new JsonObject { ["kind"] = "path", ["path"] = path } }.ToJsonString();

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        return (await Json(response))["code"]!.GetValue<string>();
    }

    [Fact]
    public async Task Answers_with_the_checklist_in_the_contract_s_shape()
    {
        _probe.ProbeAsync("C:/stickers/wide.webm", Arg.Any<CancellationToken>())
            .Returns(Samples.Sticker with { Width = 640, Height = 360, SizeBytes = 277_504, Fps = new Rational(60, 1), HasAudio = true });

        var response = await Post(PathBody("C:/stickers/wide.webm"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await Json(response);
        report["fileName"]!.GetValue<string>().Should().Be("wide.webm");
        report["bytes"]!.GetValue<long>().Should().Be(277_504);
        report["verdict"]!.GetValue<string>().Should().Be("fail");
        report["checks"]!.ToJsonString().Should().Be(new JsonArray(
            Check("container", "pass", "webm", "webm"),
            Check("codec", "pass", "vp9", "vp9"),
            Check("size", "fail", "≤ 262144", "277504"),
            Check("dimensions", "fail", "512", "640×360"),
            Check("duration", "pass", "≤ 3", "3"),
            Check("fps", "fail", "≤ 30", "60"),
            Check("audio", "fail", "none", "present"),
            Check("alpha", "pass", "any", "none")).ToJsonString());
    }

    [Fact]
    public async Task Calls_a_long_sticker_experimental()
    {
        _probe.ProbeAsync("long.webm", Arg.Any<CancellationToken>())
            .Returns(Samples.Sticker with { DurationSec = 6, DeclaredDurationSec = 2.9 });

        var response = await Post("""{"source":{"path":"long.webm","kind":"path"}}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response))["verdict"]!.GetValue<string>().Should().Be("experimental");
    }

    [Fact]
    public async Task Refuses_a_file_that_is_not_webm_with_415()
    {
        var response = await Post(PathBody("clip.mp4"));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await CodeOf(response)).Should().Be("format_unsupported");
    }

    [Fact]
    public async Task Answers_404_for_a_file_that_is_gone()
    {
        _probe.ProbeAsync("gone.webm", Arg.Any<CancellationToken>())
            .Returns<MediaInfo>(_ => throw new AppException(ErrorCode.SourceNotFound, "gone.webm does not exist"));

        var response = await Post(PathBody("gone.webm"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(response)).Should().Be("source_not_found");
    }

    [Fact]
    public async Task Answers_422_for_a_file_the_probe_cannot_read()
    {
        _probe.ProbeAsync("broken.webm", Arg.Any<CancellationToken>())
            .Returns<MediaInfo>(_ => throw new AppException(ErrorCode.SourceUnreadable, "EBML header parsing failed") { Log = "EBML header parsing failed" });

        var response = await Post(PathBody("broken.webm"));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var problem = await Json(response);
        problem["code"]!.GetValue<string>().Should().Be("source_unreadable");
        problem["log"]!.GetValue<string>().Should().Be("EBML header parsing failed");
    }

    [Fact]
    public async Task Refuses_a_path_in_web_mode()
    {
        using var web = new ApiFactory();

        var response = await Post(PathBody("C:/stickers/cat.webm"), web);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("bad_request");
        await _probe.DidNotReceiveWithAnyArgs().ProbeAsync(default!, Cancel);
    }

    [Theory]
    [InlineData("""{"source":{"path":"cat.webm"}}""")]
    [InlineData("""{"source":{"kind":"url","path":"cat.webm"}}""")]
    [InlineData("""{"source":{"kind":"path","path":"cat.webm","size":1}}""")]
    [InlineData("""{}""")]
    public async Task Refuses_a_source_the_contract_does_not_have(string body)
    {
        var response = await Post(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("bad_request");
    }

    private static JsonObject Check(string rule, string status, string expected, string actual) => new()
    {
        ["rule"] = rule,
        ["status"] = status,
        ["expected"] = expected,
        ["actual"] = actual,
    };
}
