using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StickerSnap.Application.Media;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Telegram;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Infrastructure.Projects;
using StickerSnap.Tests.Api;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// A clip of the corpus through the whole server and the real FFmpeg: «Экспорт WEBM» puts a
/// sticker Telegram takes into the stickers folder and the project, and «Точное превью» serves a
/// draft of an edit not yet saved.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealExportTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>, IAsyncLifetime
{
    private static readonly TimeSpan RenderGuard = TimeSpan.FromMinutes(2);

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-real-export-");
    private readonly ApiFactory _factory = new(("Mode", "desktop"));
    private WebApplicationFactory<StickerSnap.Api.Program> _server = null!;
    private HttpClient _client = null!;
    private Project _project = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string Stickers => Path.Combine(_files.FullName, "Стикеры");

    public async ValueTask InitializeAsync()
    {
        _server = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
            .RemoveAll<IStickerRenderer>()
            .AddSingleton<IStickerRenderer>(new FfmpegStickerRenderer(ffmpeg.Ffmpeg))
            .RemoveAll<IMediaProbe>()
            .AddSingleton(ffmpeg.Probe)));
        _client = _server.CreateClient();

        var clip = await TestFiles.CopyToAsync("Corpus/01-cat.gif", _files.FullName, Cancel);
        var media = await ffmpeg.Probe.ProbeAsync(clip, Cancel);
        _project = new Project(
            Guid.NewGuid(), "Кот", Revision: 1, DateTimeOffset.UtcNow,
            new ProjectSource(clip, "01-cat.gif", "v1:test", media), DefaultScene.For(media));
        var path = Path.Combine(_files.FullName, "Кот.ssproj");
        await File.WriteAllBytesAsync(path, ProjectFile.Current.Write(_project), Cancel);

        (await _client.PostAsync("/api/v1/projects/open", Body(new JsonObject { ["path"] = path }), Cancel))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PatchAsync("/api/v1/settings", Body(new JsonObject { ["folders"] = new JsonObject { ["stickers"] = Stickers } }), Cancel))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _factory.DisposeAsync();
        _files.Delete(recursive: true);
    }

    private static StringContent Body(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private async Task<JsonNode> RunAsync(JsonObject request, string end = "succeeded")
    {
        await using var updates = await JobUpdates.ConnectAsync(_server.Server);
        var created = await _client.PostAsync("/api/v1/jobs", Body(request), Cancel);
        created.StatusCode.Should().Be(HttpStatusCode.Accepted, await created.Content.ReadAsStringAsync(Cancel));
        return JsonNode.Parse((await updates.UntilAsync(end, RenderGuard)).GetRawText())!;
    }

    [Fact]
    public async Task Exports_a_sticker_telegram_takes_into_the_stickers_folder_and_the_project()
    {
        var done = await RunAsync(new JsonObject
        {
            ["kind"] = "export",
            ["projectId"] = _project.Id.ToString(),
            ["revision"] = 1,
            ["segmentIds"] = new JsonArray("seg-1"),
        });

        var result = done["items"]![0]!["export"]!;
        var path = Path.Combine(Stickers, "Кот — Стикер 1.webm");
        result["path"]!.GetValue<string>().Should().Be(path);
        result["validation"]!["verdict"]!.GetValue<string>().Should().Be("pass", result["validation"]!.ToJsonString());
        var bytes = result["bytes"]!.GetValue<long>();
        bytes.Should().Be(new FileInfo(path).Length).And.BeLessThanOrEqualTo(TelegramLimits.Current.Sticker.MaxBytes);
        var sticker = await ffmpeg.Probe.ProbeAsync(path, Cancel);
        sticker.Format.Should().Be(MediaFormat.Webm);
        sticker.Codec.Should().Be("vp9");
        Math.Max(sticker.Width, sticker.Height).Should().Be(512);

        var served = await _client.GetByteArrayAsync(result["fileUrl"]!.GetValue<string>(), Cancel);
        served.Should().Equal(await File.ReadAllBytesAsync(path, Cancel));
        var project = JsonNode.Parse(await _client.GetStringAsync($"/api/v1/projects/{_project.Id}", Cancel))!;
        project["revision"]!.GetValue<int>().Should().Be(1);
        project["exports"]!["seg-1"]!["stale"]!.GetValue<bool>().Should().BeFalse();
        Directory.EnumerateFiles(Stickers).Should().ContainSingle();
    }

    [Fact]
    public async Task Previews_an_edit_not_saved_as_a_draft_it_serves()
    {
        var segment = JsonNode.Parse(await _client.GetStringAsync($"/api/v1/projects/{_project.Id}", Cancel))!["scene"]!["segments"]![0]!;
        segment["speed"] = 2;

        var done = await RunAsync(new JsonObject { ["kind"] = "preview", ["projectId"] = _project.Id.ToString(), ["segment"] = segment.DeepClone() });

        var preview = done["items"]![0]!["preview"]!;
        preview["bytes"]!.GetValue<long>().Should().BePositive().And.BeLessThanOrEqualTo(TelegramLimits.Current.Sticker.MaxBytes);
        var draft = await _client.GetByteArrayAsync(preview["fileUrl"]!.GetValue<string>(), Cancel);
        var copy = Path.Combine(_files.FullName, "draft.webm");
        await File.WriteAllBytesAsync(copy, draft, Cancel);
        (await ffmpeg.Probe.ProbeAsync(copy, Cancel)).Codec.Should().Be("vp9");
        Directory.EnumerateFiles(Stickers).Should().BeEmpty();
    }
}
