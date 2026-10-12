using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Render;

/// <summary>
/// What becomes the sticker, as <see cref="SceneCompiler"/> works it out from a segment: which
/// frames of the source play and how, what part of the picture is kept, at what size and rate.
/// Nothing in it is about the engine that renders it.
/// </summary>
/// <param name="Crop">
/// The part of the picture kept, in the source's pixels as shown; the whole picture for a stretch
/// </param>
/// <param name="Width">
/// Of the sticker; both sides even, one of them the preset's side. Another proportion than the
/// crop's squeezes the picture
/// </param>
/// <param name="KeepAlpha">The source can be transparent, and the sticker stays so</param>
/// <param name="Detail">
/// The size the picture is shrunk to before it is brought back to <paramref name="Width"/> ×
/// <paramref name="Height"/>; null keeps every detail
/// </param>
public sealed record StickerSpec(
    StickerClip Clip,
    PixelRect Crop,
    int Width,
    int Height,
    int Fps,
    bool KeepAlpha,
    PixelSize? Detail = null)
{
    public PixelSize Size => new(Width, Height);

    /// <summary>The frames one forward run plays at <see cref="Fps"/>.</summary>
    public int ForwardFrames => Clip.ForwardFrames(Fps);

    /// <summary>The frames of the file: the whole loop.</summary>
    public int PlayedFrames => Clip.PlayedFrames(Fps);

    /// <summary>How long the file plays: what the bitrate and Telegram's 3 s are measured on.</summary>
    public double DurationSec => Clip.Seconds(Fps);

    /// <summary>
    /// The sticker a source makes before any edit: its first frames up to the preset's limit, the
    /// largest square in the middle of the picture, scaled to the preset's side at the
    /// <see cref="StartRate">rate of the source</see>. A side of the square is even, as yuv420 needs.
    /// </summary>
    public static StickerSpec Default(MediaInfo source, PresetLimits limits)
    {
        if (source.Width < 2 || source.Height < 2)
            throw new ArgumentException($"A picture of {source.Width}x{source.Height} has no square to crop", nameof(source));
        if (source.FrameCount < 1) throw new ArgumentException("A source without frames makes no sticker", nameof(source));

        var side = Math.Min(source.Width, source.Height) & ~1;
        var crop = new FrameRect((source.Width - side) / 2, (source.Height - side) / 2, side, side);
        var segment = new Segment(
            "default", "default", 0, source.FrameCount - 1, [], new SegmentFrame.Crop(CropPreset.Square, crop),
            Speed: 1, LoopMode.Normal, SegmentPreset.Sticker, LongSticker: false, AutoFit: true);
        return SceneCompiler.Compile(segment, source, limits);
    }

    /// <summary>
    /// The frame rate a sticker of the source plays at: the rate its frames come at after the
    /// speed, when that is a steady whole number under the limit, so a 10 fps GIF stays 10 fps
    /// instead of repeating each frame three times and shows no fps concession it never made; the
    /// limit for any other source.
    /// </summary>
    /// <param name="speed">A 30 fps video at half speed brings 15 frames a second</param>
    public static int StartRate(MediaInfo source, int maxFps, double speed = 1)
    {
        if (source.IsVariableFrameRate || !source.Fps.IsValid) return maxFps;
        var rate = source.Fps.ToDouble() * speed;
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
