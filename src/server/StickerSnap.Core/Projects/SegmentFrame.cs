namespace StickerSnap.Core.Projects;

/// <summary>R3, «Кадр»: which part of the picture becomes the sticker, told apart by `mode` in the contract.</summary>
public abstract record SegmentFrame
{
    private SegmentFrame()
    {
    }

    /// <summary>A rectangle of the picture is kept as it is.</summary>
    /// <param name="Rect">In the source's pixels as shown</param>
    public sealed record Crop(CropPreset Preset, FrameRect Rect) : SegmentFrame;

    /// <summary>The whole picture is squeezed to a proportion.</summary>
    /// <param name="Aspect">Parts, not pixels: the longer side becomes the preset's side</param>
    public sealed record Stretch(StretchPreset Preset, FrameAspect Aspect) : SegmentFrame;
}

/// <summary>The button that made a crop: «Квадрат», «Как в источнике», or a free drag.</summary>
public enum CropPreset
{
    Square,
    Source,
    Free,
}

/// <summary>The button that made a stretch: «Как в источнике», «Сплющить в 1:1», or a free drag.</summary>
public enum StretchPreset
{
    Source,
    Squash,
    Free,
}

/// <summary>
/// A rectangle as the SPA sends it. Numbers, not whole pixels, because JSON has no others; the
/// scene is valid only when they are whole (<see cref="SceneValidator"/>).
/// </summary>
public readonly record struct FrameRect(double X, double Y, double Width, double Height);

/// <summary>A proportion as two parts: 16 to 9, or 512 to 288.</summary>
public readonly record struct FrameAspect(double Width, double Height);
