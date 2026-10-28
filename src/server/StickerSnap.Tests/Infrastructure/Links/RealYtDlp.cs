using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Infrastructure.Links;
using StickerSnap.Tests.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Links;

/// <summary>
/// The yt-dlp of StickerSnap__YtDlpDir, or else the one `npm run fetch:ytdlp` put into
/// tools/yt-dlp/&lt;rid&gt;, with the adapter over it, and a web server on 127.0.0.1 serving a
/// 1-second WEBM the pinned FFmpeg makes: yt-dlp takes a link to a file as a video of its own,
/// so its whole run is checked without the internet. Without yt-dlp the tests fail, telling how to get it.
/// </summary>
public sealed class RealYtDlp : IAsyncLifetime
{
    /// <summary>How long /slow.webm takes per 4 KB, so a download can be stopped half way.</summary>
    private static readonly TimeSpan Drip = TimeSpan.FromMilliseconds(50);

    private readonly RealFfmpeg _ffmpeg = new();
    private WebApplication? _server;

    public RealYtDlp()
    {
        var ytDlp = new YtDlpLocator(Options.Create(new YtDlpOptions { YtDlpDir = YtDlpDirectory() }), NullLogger<YtDlpLocator>.Instance);
        var ffmpeg = new FfmpegLocator(
            Options.Create(new FfmpegOptions { FfmpegDir = Environment.GetEnvironmentVariable("StickerSnap__FfmpegDir") }),
            new CliFfmpegVersionReader(),
            NullLogger<FfmpegLocator>.Instance);
        Site = new YtDlpVideoSite(ytDlp, ffmpeg, NullLogger<YtDlpVideoSite>.Instance);
    }

    internal YtDlpVideoSite Site { get; }

    /// <summary>The served clip, as it is on disk.</summary>
    public string ClipPath { get; private set; } = "";

    /// <summary>http://127.0.0.1:&lt;port&gt;/</summary>
    public string BaseUrl { get; private set; } = "";

    public string Clip(string name) => _ffmpeg.Clip(name);

    public async ValueTask InitializeAsync()
    {
        ClipPath = await _ffmpeg.MakeAsync(
            "clip.webm", "-f", "lavfi", "-i", "testsrc2=s=320x240:r=30:d=1", "-c:v", "libvpx-vp9", "-b:v", "400k");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _server = builder.Build();
        _server.MapGet("/clip.webm", () => Results.File(ClipPath, "video/webm", enableRangeProcessing: true));
        _server.MapGet("/slow.webm", async (HttpContext context) =>
        {
            var bytes = await File.ReadAllBytesAsync(ClipPath);
            context.Response.ContentType = "video/webm";
            context.Response.ContentLength = bytes.Length;
            foreach (var chunk in bytes.Chunk(4096))
            {
                await context.Response.Body.WriteAsync(chunk, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(Drip, context.RequestAborted);
            }
        });
        await _server.StartAsync();
        BaseUrl = _server.Urls.Single().TrimEnd('/') + "/";
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
        await _ffmpeg.DisposeAsync();
    }

    private static string YtDlpDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("StickerSnap__YtDlpDir");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var rid = $"{(OperatingSystem.IsWindows() ? "win" : "linux")}-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var fetched = Path.Combine(directory.FullName, "tools", "yt-dlp", rid);
            if (Directory.Exists(fetched)) return fetched;
        }

        throw new InvalidOperationException(
            $"No yt-dlp for the slow tests: run npm run fetch:ytdlp (tools/yt-dlp/{rid}) or set StickerSnap__YtDlpDir");
    }
}
