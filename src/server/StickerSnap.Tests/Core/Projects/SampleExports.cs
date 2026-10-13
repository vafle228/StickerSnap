using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Core.Validation;

namespace StickerSnap.Tests.Core.Projects;

/// <summary>An export of a segment as the engine would make it: second step of the ladder, checked green.</summary>
internal static class SampleExports
{
    public static readonly FitRung First = new(30, 40, new PixelSize(512, 512), []);

    public static readonly FitRung Second = new(24, 40, new PixelSize(512, 512), [new Concession.Fps(30, 24)]);

    public static readonly StickerValidation Green = new(
        SegmentPreset.Sticker,
        ValidationVerdict.Pass,
        [
            new RuleCheck(ValidationRule.Container, CheckStatus.Pass, "webm", "webm"),
            new RuleCheck(ValidationRule.Size, CheckStatus.Pass, $"≤ {TelegramLimits.Current.Sticker.MaxBytes}", "190464"),
        ]);

    /// <summary>The export of <paramref name="segment"/> cut from <paramref name="source"/>, made at revision 12.</summary>
    public static SegmentExport Of(Segment segment, ProjectSource source, string fileId = "0f8e1d2c3b4a59687766554433221100") => new(
        segment.Id,
        SceneRevision: 12,
        SegmentSignature.Of(segment, source),
        fileId,
        Path.Combine(Path.GetTempPath(), $"Кот и клавиатура — {segment.Name}.webm"),
        Bytes: 190_464,
        new StickerOutput(512, 512, 24, 3),
        Crf: 40,
        Second.Concessions,
        [new LadderTry(First, 300_000), new LadderTry(Second, 190_464)],
        Green);
}
