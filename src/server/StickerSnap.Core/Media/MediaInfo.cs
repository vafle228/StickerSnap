namespace StickerSnap.Core.Media;

/// <summary>What the media engine tells about a source's picture.</summary>
/// <param name="Codec">The video codec: `h264`, `vp9`, `gif`, `webp`</param>
/// <param name="Width">As shown: rotation and a non-square pixel aspect ratio already applied</param>
/// <param name="Fps">The average frame rate; for a VFR source it differs from the stream's base rate</param>
/// <param name="IsVariableFrameRate">Frames are not evenly spaced, so frame n does not sit at n/fps</param>
/// <param name="DurationSec">To the end of the last frame: how long the file really plays</param>
/// <param name="HasAlpha">
/// The picture can be transparent: a video with an alpha channel (VP8 and VP9 in WEBM), or a GIF or
/// WEBP with a pixel that is really transparent.
/// </param>
public sealed record MediaInfo(
    MediaFormat Format,
    string Codec,
    int Width,
    int Height,
    Rational Fps,
    bool IsVariableFrameRate,
    int FrameCount,
    double DurationSec,
    bool HasAlpha,
    bool HasAudio,
    long SizeBytes)
{
    /// <summary>
    /// The length the container's header declares, when the probe reads it apart from the frames:
    /// WEBM's Segment › Info › Duration, which Telegram takes for a sticker's length. A long
    /// sticker (R19) declares at most 3 s there while its frames run longer. Null when the
    /// container has no such header; <see cref="DurationSec"/> always runs to the end of the last frame.
    /// </summary>
    public double? DeclaredDurationSec { get; init; }
}
