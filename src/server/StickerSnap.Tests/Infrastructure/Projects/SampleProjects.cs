using System.Text;
using AwesomeAssertions.Equivalency;
using StickerSnap.Core.Projects;
using StickerSnap.Tests.Core.Projects;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Projects;

/// <summary>TestData/Projects/cat-keyboard.ssproj, as text and as the project it holds.</summary>
internal static class SampleProjects
{
    public const string SamplePath = "Projects/cat-keyboard.ssproj";

    public static readonly Project Sample = new(
        Guid.Parse("6f1c2a4e-8b3d-4f7a-9c21-5d0e7a3b9f12"),
        "Кот и клавиатура",
        Revision: 12,
        SavedAt: new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero),
        new ProjectSource(
            @"D:\Видео\cat-keyboard.gif",
            "cat-keyboard.gif",
            "sha256:5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8",
            Scenes.Source),
        Scenes.Of(
            Scenes.Segment,
            new Segment(
                "seg-2", "Стикер 2", InFrame: 10, OutFrame: 20, ExcludedFrames: [],
                new SegmentFrame.Stretch(StretchPreset.Squash, new FrameAspect(270, 270)),
                Speed: 1.5, LoopMode.Pingpong, SegmentPreset.Emoji, LongSticker: false, AutoFit: true)));

    /// <summary>With LF line ends, whatever git made of them on checkout.</summary>
    public static string Text(string path) => TestFiles.ReadText(path).ReplaceLineEndings("\n");

    public static byte[] Bytes(string path) => Encoding.UTF8.GetBytes(Text(path));

    /// <summary>
    /// Records compared field by field rather than by Equals, which compares their lists by
    /// reference, and a crop or a stretch by what it is rather than as a bare SegmentFrame.
    /// </summary>
    public static EquivalencyOptions<Project> ByValue(EquivalencyOptions<Project> options) =>
        options.ComparingRecordsByMembers().PreferringRuntimeMemberTypes();
}
