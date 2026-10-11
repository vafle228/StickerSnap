using StickerSnap.Core.Errors;
using StickerSnap.Core.Ffmpeg;

namespace StickerSnap.Application.Ffmpeg;

public enum FfmpegStatus
{
    Ok,
    Missing,
    TooOld,
}

/// <summary>What the search for FFmpeg found.</summary>
/// <param name="Directory">Where ffmpeg and ffprobe lie; null when none was found</param>
public sealed record FfmpegLookup(FfmpegStatus Status, FfmpegVersion? Version, string? Directory)
{
    public static readonly FfmpegLookup Missing = new(FfmpegStatus.Missing, null, null);

    public static FfmpegLookup Found(FfmpegVersion version, string directory) =>
        new(version.IsSupported ? FfmpegStatus.Ok : FfmpegStatus.TooOld, version, directory);

    /// <summary>
    /// The directory of a usable FFmpeg; otherwise `ffmpeg_missing` (503), the answer of every
    /// request that needs FFmpeg while it is not there or older than 9.0.
    /// </summary>
    public string Require() => Status == FfmpegStatus.Ok && Directory is not null
        ? Directory
        : throw new AppException(
            ErrorCode.FfmpegMissing,
            Status == FfmpegStatus.TooOld
                ? $"FFmpeg {Version?.Text} in {Directory} is older than {FfmpegVersion.MinimumMajor}.0"
                : "FFmpeg was not found");
}
