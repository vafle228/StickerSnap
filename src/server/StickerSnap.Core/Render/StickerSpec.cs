using StickerSnap.Core.Media;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Render;

/// <summary>What part of a source becomes the sticker, and at what size and rate.</summary>
/// <param name="StartSec">Where the sticker starts in the source</param>
/// <param name="Crop">The part of the picture kept, in the source's pixels as shown</param>
/// <param name="Width">Of the sticker; both sides even, one of them the preset's side</param>
/// <param name="KeepAlpha">The source can be transparent, and the sticker stays so</param>
/// <param name="Detail">
/// The size the picture is shrunk to before it is brought back to <paramref name="Width"/> ×
/// <paramref name="Height"/>; null keeps every detail
/// </param>
public sealed record StickerSpec(
    double StartSec,
    double DurationSec,
    PixelRect Crop,
    int Width,
    int Height,
    int Fps,
    bool KeepAlpha,
    PixelSize? Detail = null)
{
    public PixelSize Size => new(Width, Height);

    /// <summary>
    /// The sticker a source makes before any edit: its first seconds up to the preset's limit,
    /// the largest square in the middle of the picture, scaled to the preset's side at the
    /// <see cref="StartRate">rate of the source</see>. A side of the square is even, as yuv420 needs.
    /// </summary>
    public static StickerSpec Default(MediaInfo source, PresetLimits limits)
    {
        if (source.Width < 2 || source.Height < 2)
            throw new ArgumentException($"A picture of {source.Width}x{source.Height} has no square to crop", nameof(source));

        var side = Math.Min(source.Width, source.Height) & ~1;
        var crop = new PixelRect((source.Width - side) / 2, (source.Height - side) / 2, side, side);
        var duration = source.DurationSec > 0 ? Math.Min(source.DurationSec, limits.MaxDurationSec) : limits.MaxDurationSec;

        return new StickerSpec(0, duration, crop, limits.Side, limits.Side, StartRate(source, limits.MaxFps), source.HasAlpha);
    }

    /// <summary>
    /// The frame rate a sticker of the source plays at: its own when that is a steady whole number
    /// under the limit, so a 10 fps GIF stays 10 fps instead of repeating each frame three times
    /// and shows no fps concession it never made; the limit for any other source.
    /// </summary>
    public static int StartRate(MediaInfo source, int maxFps)
    {
        if (source.IsVariableFrameRate || !source.Fps.IsValid) return maxFps;
        var rate = source.Fps.ToDouble();
        var whole = Math.Round(rate);
        return Math.Abs(rate - whole) < 0.01 && whole >= 1 && whole < maxFps ? (int)whole : maxFps;
    }

    /// <summary>This sticker at the frame rate and detail of a step of the fit ladder.</summary>
    public StickerSpec At(FitRung rung) => this with
    {
        Fps = rung.Fps,
        Detail = rung.Detail == Size ? null : rung.Detail,
    };
}
