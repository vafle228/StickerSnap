namespace StickerSnap.Core.Media;

/// <summary>What the media engine tells about a source's picture.</summary>
/// <param name="Codec">The video codec: `h264`, `vp9`, `gif`, `webp`</param>
/// <param name="Width">As shown: rotation and a non-square pixel aspect ratio already applied</param>
/// <param name="Fps">The average frame rate; for a VFR source it differs from the stream's base rate</param>
/// <param name="IsVariableFrameRate">Frames are not evenly spaced, so frame n does not sit at n/fps</param>
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
    long SizeBytes);
