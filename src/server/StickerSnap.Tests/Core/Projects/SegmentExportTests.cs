using StickerSnap.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Core.Projects;

/// <summary>
/// When an export goes stale: only an edit of its own segment that changes the sticker, its
/// removal, or another video makes it so; a save of the other segments or a rename does not.
/// </summary>
public sealed class SegmentExportTests
{
    private static readonly Project Project = SampleProjects.Sample;
    private static readonly Segment First = Project.Scene.Segments[0];
    private static readonly Segment Second = Project.Scene.Segments[1];

    private static Project WithSegments(params Segment[] segments) => Project with { Scene = Scenes.Of(segments) };

    [Fact]
    public void Is_fresh_while_its_segment_is_as_exported()
    {
        SampleExports.Of(First, Project.Source).IsStaleIn(Project).Should().BeFalse();
    }

    [Fact]
    public void Stays_fresh_when_another_segment_is_edited()
    {
        var export = SampleExports.Of(First, Project.Source);

        export.IsStaleIn(WithSegments(First, Second with { OutFrame = 15 })).Should().BeFalse();
    }

    [Fact]
    public void Stays_fresh_when_its_segment_is_only_renamed()
    {
        var export = SampleExports.Of(First, Project.Source);

        export.IsStaleIn(WithSegments(First with { Name = "Прыжок" }, Second)).Should().BeFalse();
    }

    public static TheoryData<string, Segment> Edits => new()
    {
        { "in", Scenes.Segment with { InFrame = 1 } },
        { "out", Scenes.Segment with { OutFrame = 28 } },
        { "excluded", Scenes.Segment with { ExcludedFrames = [3] } },
        { "crop", Scenes.Segment with { Frame = new SegmentFrame.Crop(CropPreset.Free, new FrameRect(104, 0, 270, 270)) } },
        { "stretch", Scenes.Segment with { Frame = new SegmentFrame.Stretch(StretchPreset.Source, new FrameAspect(16, 9)) } },
        { "speed", Scenes.Segment with { Speed = 1.25 } },
        { "loop", Scenes.Segment with { Loop = LoopMode.Pingpong } },
        { "preset", Scenes.Segment with { Preset = SegmentPreset.Emoji } },
        { "long", Scenes.Segment with { LongSticker = true } },
        { "autoFit", Scenes.Segment with { AutoFit = false } },
    };

    [Theory]
    [MemberData(nameof(Edits))]
    public void Goes_stale_when_its_segment_is_edited(string edit, Segment edited)
    {
        var export = SampleExports.Of(First, Project.Source);

        export.IsStaleIn(WithSegments(edited, Second)).Should().BeTrue(edit);
    }

    [Fact]
    public void Goes_stale_when_its_segment_is_removed()
    {
        SampleExports.Of(First, Project.Source).IsStaleIn(WithSegments(Second)).Should().BeTrue();
    }

    [Fact]
    public void Goes_stale_when_the_project_takes_another_video()
    {
        var export = SampleExports.Of(First, Project.Source);

        export.IsStaleIn(Project with { Source = Project.Source with { Fingerprint = "v1:other" } }).Should().BeTrue();
    }

    [Fact]
    public void Gives_the_same_signature_to_the_same_sticker_and_says_its_version()
    {
        var signature = SegmentSignature.Of(First, Project.Source);

        signature.Should().Be(SegmentSignature.Of(First with { Name = "Другое имя" }, Project.Source));
        signature.Should().MatchRegex("^v1:[0-9a-f]{32}$");
    }

    [Fact]
    public void Becomes_the_last_export_of_its_segment_without_a_new_revision()
    {
        var older = SampleExports.Of(First, Project.Source, fileId: "a");
        var newer = SampleExports.Of(First, Project.Source, fileId: "b");

        var project = Project.WithExport(older).WithExport(newer).WithExport(SampleExports.Of(Second, Project.Source));

        project.Revision.Should().Be(Project.Revision);
        project.Exports.Keys.Should().BeEquivalentTo("seg-1", "seg-2");
        project.Exports["seg-1"].Should().BeSameAs(newer);
        Project.Exports.Should().BeEmpty();
    }

    [Fact]
    public void Names_its_file_by_the_file_s_own_name()
    {
        SampleExports.Of(First, Project.Source).FileName.Should().Be("Кот и клавиатура — Стикер 1.webm");
    }
}
