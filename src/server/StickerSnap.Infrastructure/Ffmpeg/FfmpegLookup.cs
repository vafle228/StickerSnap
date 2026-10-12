using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>What the search for FFmpeg found.</summary>
/// <param name="Directory">Where ffmpeg and ffprobe lie; null when none was found</param>
internal sealed record FfmpegLookup(MediaEngineStatus Status, FfmpegVersion? Version, string? Directory)
{
    public static readonly FfmpegLookup Missing = new(MediaEngineStatus.Missing, null, null);

    public static FfmpegLookup Found(FfmpegVersion version, string directory) =>
        new(version.IsSupported ? MediaEngineStatus.Ok : MediaEngineStatus.TooOld, version, directory);

    /// <summary>
    /// The directory of a usable FFmpeg; otherwise `ffmpeg_missing` (503), the answer of every
    /// request that needs FFmpeg while it is not there or older than 9.0.
    /// </summary>
    public string Require() => Status == MediaEngineStatus.Ok && Directory is not null
        ? Directory
        : throw new AppException(
            ErrorCode.FfmpegMissing,
            Status == MediaEngineStatus.TooOld
                ? $"FFmpeg {Version?.Text} in {Directory} is older than {FfmpegVersion.MinimumMajor}.0"
                : "FFmpeg was not found");
}
