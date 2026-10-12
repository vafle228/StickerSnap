using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Tests.Core.Projects;

/// <summary>A valid scene of a 480×270, 10 fps GIF of 31 frames, varied per test with `with`.</summary>
internal static class Scenes
{
    public static readonly MediaInfo Source = new(
        MediaFormat.Gif, "gif", 480, 270, new Rational(10, 1), IsVariableFrameRate: false,
        FrameCount: 31, DurationSec: 3.1, HasAlpha: false, HasAudio: false, SizeBytes: 1_048_576);

    public static readonly Segment Segment = new(
        "seg-1", "Стикер 1", InFrame: 0, OutFrame: 29, ExcludedFrames: [],
        new SegmentFrame.Crop(CropPreset.Square, new FrameRect(105, 0, 270, 270)),
        Speed: 1, LoopMode.Normal, SegmentPreset.Sticker, LongSticker: false, AutoFit: true);

    public static readonly Scene Valid = Of(Segment);

    public static Scene Of(params Segment[] segments) =>
        new(Scene.CurrentSchemaVersion, [new Layer.Video("video")], segments);
}
