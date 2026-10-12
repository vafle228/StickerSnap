using System.Globalization;
using System.Text.RegularExpressions;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;

namespace StickerSnap.Infrastructure.Ffmpeg;

internal sealed partial class FfprobeMediaProbe(IFfmpegLocator locator, IFfmpeg ffmpeg) : IMediaProbe
{
    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (!MediaFormats.IsSupportedExtension(extension))
            throw new AppException(ErrorCode.FormatUnsupported, $"{Path.GetFileName(path)} is not mp4, mov, webm, gif or webp")
            {
                Params = new Dictionary<string, object> { ["ext"] = extension },
            };
        if (!File.Exists(path)) throw new AppException(ErrorCode.SourceNotFound, $"{path} does not exist");

        var directory = (await locator.FindAsync(cancellationToken)).Require();
        var probe = await FfmpegProcess.RunAsync(
            Path.Combine(directory, FfmpegProcess.Ffprobe), [.. FfprobeOutput.Arguments, path], null, cancellationToken);
        if (!probe.IsSuccess)
            throw new AppException(ErrorCode.SourceUnreadable, $"ffprobe could not read {path} (exit code {probe.ExitCode})")
            {
                Log = probe.Log,
            };

        var media = FfprobeOutput.Parse(probe.Output, extension);
        if (media.Format == MediaFormat.Webp) media = await WithFrameDelaysAsync(directory, path, media, cancellationToken);
        return media is { Format: MediaFormat.Gif or MediaFormat.Webp, HasAlpha: true }
            ? media with { HasAlpha = await HasTransparentPixelAsync(path, cancellationToken) }
            : media;
    }

    /// <summary>Animated WEBP keeps only a delay per frame: its length and rate come from them.</summary>
    private static async Task<MediaInfo> WithFrameDelaysAsync(
        string directory, string path, MediaInfo media, CancellationToken cancellationToken)
    {
        var frames = await FfmpegProcess.RunAsync(
            Path.Combine(directory, FfmpegProcess.Ffprobe), [.. FfprobeOutput.FrameDelayArguments, path], null, cancellationToken);
        if (!frames.IsSuccess)
            throw new AppException(ErrorCode.SourceUnreadable, $"ffprobe could not read the frames of {path} (exit code {frames.ExitCode})")
            {
                Log = frames.Log,
            };

        return FfprobeOutput.WithFrameDelays(media, frames.Output);
    }

    /// <summary>
    /// GIF and WEBP always decode with an alpha channel, mostly opaque: encoders mark a colour
    /// transparent only to skip pixels that did not change. Decodes the frames and looks for a
    /// pixel that stays transparent; stickers are short, so this takes a fraction of a second.
    /// </summary>
    private async Task<bool> HasTransparentPixelAsync(string path, CancellationToken cancellationToken)
    {
        var run = await ffmpeg.RunAsync(
            [
                "-v", "error", "-i", path, "-map", "0:v:0",
                "-vf", "format=rgba,alphaextract,signalstats,metadata=print:key=lavfi.signalstats.YMIN:file=-",
                "-f", "null", "-",
            ],
            progress: null,
            cancellationToken);
        if (!run.IsSuccess)
            throw new AppException(ErrorCode.SourceUnreadable, $"ffmpeg could not decode {path} (exit code {run.ExitCode})")
            {
                Log = run.Log,
            };

        return AlphaMinimum().Matches(run.Output)
            .Any(m => int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture) < 255);
    }

    [GeneratedRegex(@"lavfi\.signalstats\.YMIN=(?<min>\d+)")]
    private static partial Regex AlphaMinimum();
}
