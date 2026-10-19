using System.Globalization;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// The editor's pictures with ffmpeg: strips and covers as one WEBP picture each (libwebp, `tile`
/// for a strip), the playable copy as a realtime VP9 WEBM with alpha where the source has it.
/// </summary>
internal sealed class FfmpegEditorMedia(IFfmpeg ffmpeg) : IEditorMedia
{
    /// <summary>libwebp's quality for the strips: small files for the timeline's tiny cells</summary>
    internal const int StripQuality = 70;

    internal const int CoverQuality = 85;

    /// <summary>VP9's constant quality for the copy: the editor shows it, the sticker is rendered from the source</summary>
    internal const int PlayableCrf = 32;

    /// <summary>How the C library words ENOSPC, which ffmpeg prints when a write fails, on every system.</summary>
    private const string NoSpace = "No space left on device";

    public Task WriteStripAsync(string sourcePath, MediaInfo source, FrameStrip strip, string outputPath, CancellationToken cancellationToken) =>
        RunAsync(StripArguments(sourcePath, source, strip, outputPath), "the frames", sourcePath, outputPath, cancellationToken);

    public Task WriteCoverAsync(string sourcePath, MediaInfo source, CoverShot cover, string outputPath, CancellationToken cancellationToken) =>
        RunAsync(CoverArguments(sourcePath, source, cover, outputPath), "the cover", sourcePath, outputPath, cancellationToken);

    public Task WritePlayableAsync(string sourcePath, MediaInfo source, string outputPath, CancellationToken cancellationToken) =>
        RunAsync(PlayableArguments(sourcePath, source, outputPath), "the playable copy", sourcePath, outputPath, cancellationToken);

    /// <summary>The strip's frames scaled to its cells, put side by side by `tile`; a short end is filled by `tile` itself.</summary>
    internal static IReadOnlyList<string> StripArguments(string sourcePath, MediaInfo source, FrameStrip strip, string outputPath)
    {
        var filters = string.Join(',',
            SourceInput.Trim(source, strip.From, strip.Last),
            $"scale={strip.FrameWidth}:{strip.Height}:flags=bicubic",
            "setsar=1",
            $"tile={strip.Count}x1");
        return Picture(SourceInput.Arguments(sourcePath, source, source.HasAlpha, strip.From, strip.Last), filters, StripQuality, outputPath);
    }

    /// <summary>The frame at its size as shown with square pixels, cropped, then scaled to the cover.</summary>
    internal static IReadOnlyList<string> CoverArguments(string sourcePath, MediaInfo source, CoverShot cover, string outputPath)
    {
        List<string> chain = [SourceInput.Trim(source, cover.Frame, cover.Frame), $"scale={source.Width}:{source.Height}", "setsar=1"];
        if (cover.Crop != new PixelRect(0, 0, source.Width, source.Height))
            chain.Add($"crop={cover.Crop.Width}:{cover.Crop.Height}:{cover.Crop.X}:{cover.Crop.Y}:exact=1");
        chain.Add($"scale={cover.Size.Width}:{cover.Size.Height}:flags=lanczos");
        chain.Add("setsar=1");
        return Picture(SourceInput.Arguments(sourcePath, source, source.HasAlpha, cover.Frame, cover.Frame), string.Join(',', chain), CoverQuality, outputPath);
    }

    /// <summary>
    /// Every frame of the source, retimed to frame n at n/fps and written at that constant rate,
    /// cut at the frames the probe counted: the copy has the frame numbers of the source.
    /// </summary>
    internal static IReadOnlyList<string> PlayableArguments(string sourcePath, MediaInfo source, string outputPath)
    {
        var rate = SourceInput.Rate(source);
        var format = source.HasAlpha ? "yuva420p" : "yuv420p";
        return
        [
            .. SourceInput.Arguments(sourcePath, source, source.HasAlpha, 0, last: null),
            "-filter_complex", $"[0:v:0]setpts=N/({rate})/TB,scale={source.Width}:{source.Height},setsar=1,format={format}[v]",
            "-map", "[v]",
            "-r", rate,
            "-fps_mode", "cfr",
            "-frames:v", source.FrameCount.ToString(CultureInfo.InvariantCulture),
            "-an",
            "-c:v", "libvpx-vp9",
            "-deadline", "realtime",
            "-cpu-used", "8",
            "-row-mt", "1",
            "-crf", PlayableCrf.ToString(CultureInfo.InvariantCulture),
            "-b:v", "0",
            "-f", "webm",
            "-y", outputPath,
        ];
    }

    private static IReadOnlyList<string> Picture(List<string> input, string filters, int quality, string outputPath) =>
    [
        .. input,
        "-filter_complex", $"[0:v:0]{filters}[v]",
        "-map", "[v]",
        "-frames:v", "1",
        "-c:v", "libwebp",
        "-quality", quality.ToString(CultureInfo.InvariantCulture),
        "-f", "webp",
        "-y", outputPath,
    ];

    private async Task RunAsync(IReadOnlyList<string> arguments, string what, string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        FfmpegResult result;
        try
        {
            result = await ffmpeg.RunAsync(arguments, progress: null, cancellationToken);
        }
        catch
        {
            DeleteQuietly(outputPath);
            throw;
        }

        if (result.IsSuccess && File.Exists(outputPath)) return;

        DeleteQuietly(outputPath);
        var name = Path.GetFileName(sourcePath);
        throw result.Log.Contains(NoSpace, StringComparison.OrdinalIgnoreCase)
            ? new AppException(ErrorCode.DiskFull, $"No space left for {what} of {name}") { Log = result.Log, Context = result.Context }
            : new AppException(ErrorCode.RenderFailed, $"ffmpeg failed {what} of {name} (exit code {result.ExitCode})") { Log = result.Log, Context = result.Context };
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (IoErrors.IsIoError(e))
        {
            // The directory may be gone or locked: there is no file to pick up either way
        }
    }
}
