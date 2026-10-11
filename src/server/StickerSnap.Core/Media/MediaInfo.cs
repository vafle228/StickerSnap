namespace StickerSnap.Core.Media;

/// <summary>What ffprobe tells about a source's picture.</summary>
/// <param name="Codec">FFmpeg's name of the video codec: `h264`, `vp9`, `gif`, `webp`</param>
/// <param name="Width">As shown: rotation and a non-square pixel aspect ratio already applied</param>
/// <param name="Fps">The average frame rate; for a VFR source it differs from the stream's base rate</param>
/// <param name="IsVariableFrameRate">Frames are not evenly spaced, so frame n does not sit at n/fps</param>
/// <param name="HasAlpha">
/// The picture can be transparent. VP8 and VP9 in WEBM carry it in the `alpha_mode` tag, not in
/// pix_fmt, and only the libvpx decoders keep it; GIF and WEBP count only when a pixel is transparent.
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
