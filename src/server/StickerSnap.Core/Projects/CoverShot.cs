using StickerSnap.Core.Render;

namespace StickerSnap.Core.Projects;

/// <summary>
/// The picture on a project's card (contract, getProjectCover): one frame of the source as the first
/// sticker shows it, the middle of its segment, cropped by its frame or squeezed by its stretch. A
/// project without segments shows the middle of the whole video.
/// </summary>
/// <param name="Frame">Of the source, from 0</param>
/// <param name="Crop">Of the source's picture as shown; the whole picture for a stretch</param>
/// <param name="Size">Of the cover; never larger than the crop, the longer side at most <see cref="Side"/></param>
public sealed record CoverShot(int Frame, PixelRect Crop, PixelSize Size)
{
    /// <summary>Twice the card's cover, 192 px tall, for a sharp picture on a dense screen.</summary>
    public const int Side = 384;

    public static CoverShot Of(Project project)
    {
        var media = project.Source.Media;
        var whole = new PixelRect(0, 0, Math.Max(1, media.Width), Math.Max(1, media.Height));
        var last = Math.Max(0, media.FrameCount - 1);
        if (project.Scene.Segments is not [var segment, ..])
            return new CoverShot(last / 2, whole, Fit(whole.Width, whole.Height, whole.Width, whole.Height));

        var frame = Math.Clamp(segment.InFrame + (segment.OutFrame - segment.InFrame) / 2, 0, last);
        return segment.Frame switch
        {
            SegmentFrame.Crop { Rect: var rect } when Inside(rect, whole) is { } crop =>
                new CoverShot(frame, crop, Fit(crop.Width, crop.Height, crop.Width, crop.Height)),
            SegmentFrame.Stretch { Aspect: var aspect } when aspect.Width > 0 && aspect.Height > 0 =>
                new CoverShot(frame, whole, Fit(aspect.Width, aspect.Height, whole.Width, whole.Height)),
            _ => new CoverShot(frame, whole, Fit(whole.Width, whole.Height, whole.Width, whole.Height)),
        };
    }

    /// <summary>
    /// A size of the proportion <paramref name="width"/> to <paramref name="height"/>, its longer side
    /// <see cref="Side"/> or the picture's own longer side when that is smaller: a small GIF is not blown up.
    /// </summary>
    private static PixelSize Fit(double width, double height, int pictureWidth, int pictureHeight)
    {
        var longer = Math.Min(Side, Math.Max(pictureWidth, pictureHeight));
        var scale = longer / Math.Max(width, height);
        return new PixelSize(
            Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)));
    }

    /// <summary>The crop in whole pixels, cut to the picture; null when nothing of it is left.</summary>
    private static PixelRect? Inside(FrameRect rect, PixelRect whole)
    {
        var left = Math.Clamp((int)Math.Round(rect.X), 0, whole.Width);
        var top = Math.Clamp((int)Math.Round(rect.Y), 0, whole.Height);
        var right = Math.Clamp((int)Math.Round(rect.X + rect.Width), 0, whole.Width);
        var bottom = Math.Clamp((int)Math.Round(rect.Y + rect.Height), 0, whole.Height);
        return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : null;
    }
}
