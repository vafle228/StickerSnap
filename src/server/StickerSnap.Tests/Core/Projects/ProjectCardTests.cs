using StickerSnap.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Core.Projects;

/// <summary>A card is worked out from the project alone: its preset and the longest play.</summary>
public sealed class ProjectCardTests
{
    private static readonly Project Project = SampleProjects.Sample;

    private static Project With(params Segment[] segments) => Project with { Scene = Scenes.Of(segments) };

    [Fact]
    public void Takes_the_name_the_paths_and_the_save_time_of_the_project()
    {
        var card = ProjectCard.Of(Project, "/projects/cat.ssproj");

        card.Id.Should().Be(Project.Id);
        card.Name.Should().Be("Кот и клавиатура");
        card.Path.Should().Be("/projects/cat.ssproj");
        card.SourcePath.Should().Be(Project.Source.Path);
        card.UpdatedAt.Should().Be(Project.SavedAt);
        card.SegmentCount.Should().Be(2);
    }

    [Fact]
    public void Says_stickers_emoji_or_a_mix_by_the_presets_of_the_segments()
    {
        var emoji = Scenes.Segment with { Id = "seg-2", Preset = SegmentPreset.Emoji };

        ProjectCard.Of(With(Scenes.Segment), "/p").Preset.Should().Be(CardPreset.Sticker);
        ProjectCard.Of(With(emoji), "/p").Preset.Should().Be(CardPreset.Emoji);
        ProjectCard.Of(With(Scenes.Segment, emoji), "/p").Preset.Should().Be(CardPreset.Mixed);
    }

    [Fact]
    public void Counts_a_project_without_segments_as_stickers_that_play_for_nothing()
    {
        var card = ProjectCard.Of(With(), "/p");

        card.SegmentCount.Should().Be(0);
        card.Preset.Should().Be(CardPreset.Sticker);
        card.DurationSec.Should().Be(0);
    }

    [Fact]
    public void Gives_the_play_of_the_longest_segment_at_its_speed()
    {
        // 30 frames at 10 fps = 3 s; the other, 11 frames in ping-pong at 1.5×, plays 20 frames ≈ 1.33 s
        ProjectCard.Of(Project, "/p").DurationSec.Should().BeApproximately(3, 1e-9);
    }

    [Fact]
    public void Counts_a_ping_pong_forward_and_back_without_the_excluded_frames()
    {
        // 11 frames, 1 excluded: 10 forward, 18 with the way back, at 10 fps and 2×
        var segment = Scenes.Segment with
        {
            InFrame = 10, OutFrame = 20, ExcludedFrames = [15], Loop = LoopMode.Pingpong, Speed = 2,
        };

        ProjectCard.Of(With(segment), "/p").DurationSec.Should().BeApproximately(0.9, 1e-9);
    }
}
