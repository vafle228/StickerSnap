using StickerSnap.Application.Media;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Render;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Application.Render;

/// <summary>The commands a project's segments would run, from a substituted store and renderer.</summary>
public sealed class RenderPlanServiceTests
{
    private static readonly string ProjectPath = Path.Combine("projects", "cat.ssproj");

    private readonly IProjectStore _projects = Substitute.For<IProjectStore>();
    private readonly IStickerRenderer _renderer = Substitute.For<IStickerRenderer>();
    private readonly List<StickerRender> _renders = [];
    private readonly RenderPlanService _service;

    public RenderPlanServiceTests()
    {
        _service = new RenderPlanService(_projects, _renderer);
        _renderer.Describe(Arg.Any<StickerRender>()).Returns(call =>
        {
            _renders.Add(call.Arg<StickerRender>());
            return $"render {call.Arg<StickerRender>().OutputPath}";
        });
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private void Holds(params Segment[] segments) => _projects.ReadAsync(ProjectPath, Arg.Any<CancellationToken>()).Returns(new Project(
        Guid.NewGuid(), "Кот", 3, DateTimeOffset.UnixEpoch, new ProjectSource("D:/cat.gif", "cat.gif", "sha256:0", Scenes.Source), Scenes.Of(segments)));

    [Fact]
    public async Task Describes_each_segment_in_two_passes_at_the_first_step_of_the_ladder_beside_the_project()
    {
        var emoji = Scenes.Segment with { Id = "seg-2", Preset = SegmentPreset.Emoji, Loop = LoopMode.Pingpong };
        Holds(Scenes.Segment, emoji);

        var plans = await _service.DescribeAsync(ProjectPath, Cancel);

        plans.Select(plan => plan.Commands).Should().Equal(
            $"render {Path.GetFullPath(Path.Combine("projects", "cat.seg-1.webm"))}",
            $"render {Path.GetFullPath(Path.Combine("projects", "cat.seg-2.webm"))}");
        _renders.Select(render => (render.SourcePath, render.Effort, render.Spec.Size, render.Spec.Fps)).Should().Equal(
            ("D:/cat.gif", RenderEffort.Final, new PixelSize(512, 512), 10),
            ("D:/cat.gif", RenderEffort.Final, new PixelSize(100, 100), 10));
        _renders[0].Rate.Should().Be(new RateControl.TwoPass(TargetBitrate.BitsPerSecond(262_144, 3)));
    }

    [Fact]
    public async Task Tells_why_a_segment_past_the_end_of_the_source_makes_no_sticker_and_goes_on()
    {
        Holds(Scenes.Segment with { OutFrame = 40 }, Scenes.Segment with { Id = "seg-2" });

        var plans = await _service.DescribeAsync(ProjectPath, Cancel);

        plans[0].Should().BeEquivalentTo(new { Spec = (StickerSpec?)null, Commands = (string?)null });
        plans[0].Problem.Should().Contain("outFrame");
        plans[1].Commands.Should().NotBeNull();
    }
}
