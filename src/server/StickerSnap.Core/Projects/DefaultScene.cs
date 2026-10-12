using StickerSnap.Core.Media;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Projects;

/// <summary>
/// The scene a new project opens with (3c, B): one sticker from the first seconds of the video,
/// cut to its middle square, as the editor would make it with its default buttons.
/// </summary>
public static class DefaultScene
{
    public const string LayerId = "layer-1";
    public const string SegmentId = "seg-1";
    public const string SegmentName = "Стикер 1";

    /// <summary>
    /// One segment from frame 0: as many whole frames as fit into Telegram's 3 seconds, or the whole
    /// video when it is shorter. A square crop in the middle of the picture, the whole picture for a
    /// square source; speed 1, a normal loop, the sticker preset, the fit ladder on.
    /// </summary>
    /// <param name="source">What the media engine read; it must have at least one frame</param>
    public static Scene For(MediaInfo source)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(source.FrameCount, 1, nameof(source));

        var segment = new Segment(
            SegmentId,
            SegmentName,
            InFrame: 0,
            OutFrame: FramesToPlay(source) - 1,
            ExcludedFrames: [],
            CropOf(source.Width, source.Height),
            Speed: 1,
            LoopMode.Normal,
            SegmentPreset.Sticker,
            LongSticker: false,
            AutoFit: true);

        return new Scene(Scene.CurrentSchemaVersion, [new Layer.Video(LayerId)], [segment]);
    }

    /// <summary>
    /// Whole frames within the limit, rounded down so 29.97 fps gives 89 frames (2.97 s), not 90
    /// (3.003 s), which Telegram would refuse. A rate nobody knows takes the whole video.
    /// </summary>
    private static int FramesToPlay(MediaInfo source)
    {
        if (!source.Fps.IsValid) return source.FrameCount;
        // The tolerance keeps 3 s at 30 fps at 90 frames, whatever the last bit of the division
        var withinLimit = Math.Floor(TelegramLimits.Current.Sticker.MaxDurationSec * source.Fps.ToDouble() + 1e-9);
        return (int)Math.Clamp(withinLimit, 1, source.FrameCount);
    }

    /// <summary>The largest centred square; an odd margin leaves the extra pixel on the right or below.</summary>
    private static SegmentFrame.Crop CropOf(int width, int height)
    {
        if (width == height) return new SegmentFrame.Crop(CropPreset.Source, new FrameRect(0, 0, width, height));

        var side = Math.Min(width, height);
        return new SegmentFrame.Crop(
            CropPreset.Square,
            new FrameRect((width - side) / 2, (height - side) / 2, side, side));
    }
}
