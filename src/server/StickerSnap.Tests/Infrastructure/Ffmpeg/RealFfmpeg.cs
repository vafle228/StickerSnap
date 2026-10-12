using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Media;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// The FFmpeg of StickerSnap__FfmpegDir, or else the one `npm run fetch:ffmpeg` put into
/// tools/ffmpeg/&lt;rid&gt;, with the adapters over it and a temp directory for clips. Without
/// either the tests fail, telling how to get FFmpeg: they run only where it was asked for.
/// </summary>
public sealed class RealFfmpeg : IAsyncLifetime
{
    private readonly DirectoryInfo _clips = Directory.CreateTempSubdirectory("stickersnap-clips-");

    public RealFfmpeg()
    {
        var locator = new FfmpegLocator(
            Options.Create(new FfmpegOptions { FfmpegDir = FfmpegDirectory() }),
            new CliFfmpegVersionReader(),
            NullLogger<FfmpegLocator>.Instance);
        Ffmpeg = new CliFfmpeg(locator);
        Probe = new FfprobeMediaProbe(locator, Ffmpeg);
    }

    internal IFfmpeg Ffmpeg { get; }

    public IMediaProbe Probe { get; }

    public string Clip(string name) => Path.Combine(_clips.FullName, name);

    /// <summary>Makes a clip with lavfi: <paramref name="arguments"/> come before the output file.</summary>
    public async Task<string> MakeAsync(string name, params string[] arguments)
    {
        var path = Clip(name);
        var result = await Ffmpeg.RunAsync([.. arguments, "-y", path], progress: null, CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"ffmpeg should make {name}:\n{result.Log}");
        return path;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _clips.Delete(recursive: true);
        return ValueTask.CompletedTask;
    }

    private static string FfmpegDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("StickerSnap__FfmpegDir");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var rid = $"{(OperatingSystem.IsWindows() ? "win" : "linux")}-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var fetched = Path.Combine(directory.FullName, "tools", "ffmpeg", rid);
            if (Directory.Exists(fetched)) return fetched;
        }

        throw new InvalidOperationException(
            $"No FFmpeg for the slow tests: run npm run fetch:ffmpeg (tools/ffmpeg/{rid}) or set StickerSnap__FfmpegDir");
    }
}
