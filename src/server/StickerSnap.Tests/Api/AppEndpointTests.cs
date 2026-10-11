using System.Net;
using System.Text.Json.Nodes;
using StickerSnap.Application.Ffmpeg;
using StickerSnap.Core.Ffmpeg;

namespace StickerSnap.Tests.Api;

/// <summary>GET /api/v1/app answers with the contract's AppInfo.</summary>
public sealed class AppEndpointTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<JsonNode> GetApp(ApiFactory factory)
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/app", Cancel);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;
    }

    [Fact]
    public async Task Reports_versions_mode_bot_and_limits_in_the_contract_s_shape()
    {
        using var factory = new ApiFactory(("Mode", "desktop"));
        factory.Ffmpeg.FindAsync(Arg.Any<CancellationToken>())
            .Returns(FfmpegLookup.Found(new FfmpegVersion("9.0.2-17-g2a571b6068", 9), "/opt/ffmpeg"));

        var app = await GetApp(factory);

        app.ToJsonString().Should().Be(new JsonObject
        {
            ["appVersion"] = "0.1.0",
            ["ffmpeg"] = new JsonObject { ["status"] = "ok", ["version"] = "9.0.2-17-g2a571b6068" },
            ["apiVersion"] = "1.0.0",
            ["mode"] = "desktop",
            ["botUsername"] = "StickerSnapAppBot",
            ["limits"] = new JsonObject
            {
                ["sticker"] = new JsonObject { ["maxBytes"] = 262144, ["side"] = 512, ["maxDurationSec"] = 3, ["maxFps"] = 30 },
                ["emoji"] = new JsonObject { ["maxBytes"] = 262144, ["side"] = 100, ["maxDurationSec"] = 3, ["maxFps"] = 30 },
                ["packCapacity"] = new JsonObject { ["regular"] = 120, ["custom_emoji"] = 200 },
            },
        }.ToJsonString());
    }

    [Fact]
    public async Task Is_in_web_mode_when_no_shell_started_the_server()
    {
        using var factory = new ApiFactory();

        var app = await GetApp(factory);

        app["mode"]!.GetValue<string>().Should().Be("web");
    }

    [Fact]
    public async Task Says_ffmpeg_is_missing_without_a_version()
    {
        using var factory = new ApiFactory();

        var app = await GetApp(factory);

        app["ffmpeg"]!.ToJsonString().Should().Be("""{"status":"missing"}""");
    }

    [Fact]
    public async Task Names_the_version_of_an_ffmpeg_too_old_to_use()
    {
        using var factory = new ApiFactory();
        factory.Ffmpeg.FindAsync(Arg.Any<CancellationToken>())
            .Returns(FfmpegLookup.Found(new FfmpegVersion("7.1", 7), "/usr/bin"));

        var app = await GetApp(factory);

        app["ffmpeg"]!.ToJsonString().Should().Be("""{"status":"too_old","version":"7.1"}""");
    }
}
