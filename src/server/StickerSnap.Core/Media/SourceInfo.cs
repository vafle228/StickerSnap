namespace StickerSnap.Core.Media;

/// <summary>`missing`: «Исходник не найден» (contract, PresenceStatus).</summary>
public enum PresenceStatus
{
    Ok,
    Missing,
}

/// <summary>A project's source as the contract's SourceInfo shows it.</summary>
/// <param name="Path">Null in web mode, where the SPA has no paths</param>
public sealed record SourceInfo(
    string FileName,
    string? Path,
    PresenceStatus Status,
    MediaFormat Format,
    string Codec,
    int Width,
    int Height,
    Rational Fps,
    int FrameCount,
    double DurationSec,
    bool HasAlpha,
    long SizeBytes)
{
    /// <summary>A source that is on disk and that ffprobe has just read.</summary>
    public static SourceInfo From(MediaInfo media, string fileName, string? path) => new(
        fileName,
        path,
        PresenceStatus.Ok,
        media.Format,
        media.Codec,
        media.Width,
        media.Height,
        media.Fps,
        media.FrameCount,
        media.DurationSec,
        media.HasAlpha,
        media.SizeBytes);
}
