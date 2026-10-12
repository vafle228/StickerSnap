using StickerSnap.Core.Media;

namespace StickerSnap.Tests.Core.Render;

/// <summary>What a probe could read from a source or a sticker, varied per test with `with`.</summary>
internal static class Media
{
    public static readonly MediaInfo Landscape = new(
        MediaFormat.Mp4, "h264", 1920, 1080, new Rational(30, 1), IsVariableFrameRate: false,
        FrameCount: 150, DurationSec: 5, HasAlpha: false, HasAudio: true, SizeBytes: 3_000_000);

    public static readonly MediaInfo Sticker = new(
        MediaFormat.Webm, "vp9", 512, 512, new Rational(30, 1), IsVariableFrameRate: false,
        FrameCount: 90, DurationSec: 3, HasAlpha: false, HasAudio: false, SizeBytes: 250_000);
}
