using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Infrastructure.Links;

/// <summary>Finds the yt-dlp the server downloads videos by link with.</summary>
internal interface IYtDlpLocator
{
    /// <summary>The executable to run.</summary>
    /// <exception cref="AppException">site_changed when there is none: the import cannot work and is worth a report</exception>
    string Find();
}

/// <summary>
/// Looks for yt-dlp in StickerSnap__YtDlpDir, then next to the server (where the installer puts
/// it), then on PATH: the same order as FFmpeg. Looked for on every download, so a yt-dlp put in
/// place later needs no restart; it is a few file checks against a download of seconds.
/// </summary>
internal sealed class YtDlpLocator(IOptions<YtDlpOptions> options, ILogger<YtDlpLocator> logger) : IYtDlpLocator
{
    public static readonly string Executable = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

    public string Find()
    {
        var directories = FfmpegLocator.Directories(
            options.Value.YtDlpDir, AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"));
        var found = Find(directories);
        if (found is not null) return found;

        logger.LogWarning("yt-dlp was not found: set StickerSnap__YtDlpDir or run npm run fetch:ytdlp");
        throw new AppException(ErrorCode.SiteChanged, "yt-dlp was not found")
        {
            Log = "yt-dlp was not found next to the server, in StickerSnap__YtDlpDir or on PATH",
        };
    }

    internal static string? Find(IEnumerable<string> directories) =>
        directories.Select(directory => Path.Combine(directory, Executable)).FirstOrDefault(File.Exists);
}
