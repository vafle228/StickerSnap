using StickerSnap.Core.Media;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Render;

/// <summary>What part of a source becomes the sticker, and at what size and rate.</summary>
/// <param name="StartSec">Where the sticker starts in the source</param>
/// <param name="Crop">The part of the picture kept, in the source's pixels as shown</param>
/// <param name="Width">Of the sticker; both sides even, one of them the preset's side</param>
/// <param name="KeepAlpha">The source can be transparent, and the sticker stays so</param>
public sealed record StickerSpec(
    double StartSec,
    double DurationSec,
    PixelRect Crop,
    int Width,
    int Height,
    int Fps,
    bool KeepAlpha)
{
    /// <summary>
    /// The sticker a source makes before any edit: its first seconds up to the preset's limit,
    /// the largest square in the middle of the picture, scaled to the preset's side at its
    /// highest frame rate. A side of the square is even, as yuv420 needs.
    /// </summary>
    public static StickerSpec Default(MediaInfo source, PresetLimits limits)
    {
        if (source.Width < 2 || source.Height < 2)
            throw new ArgumentException($"A picture of {source.Width}x{source.Height} has no square to crop", nameof(source));

        var side = Math.Min(source.Width, source.Height) & ~1;
        var crop = new PixelRect((source.Width - side) / 2, (source.Height - side) / 2, side, side);
        var duration = source.DurationSec > 0 ? Math.Min(source.DurationSec, limits.MaxDurationSec) : limits.MaxDurationSec;

        return new StickerSpec(0, duration, crop, limits.Side, limits.Side, limits.MaxFps, source.HasAlpha);
    }
}
