using StickerSnap.Application.Media;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Render;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Application.Render;

/// <summary>Every segment of a project file rendered beside it, over a substituted store, renderer and probe.</summary>
public sealed class ProjectRenderServiceTests
{
    private static readonly string ProjectPath = Path.Combine("projects", "cat.ssproj");

    private readonly IProjectStore _projects = Substitute.For<IProjectStore>();
    private readonly IStickerRenderer _renderer = Substitute.For<IStickerRenderer>();
    private readonly ProjectRenderService _service;

    public ProjectRenderServiceTests()
    {
        _service = new ProjectRenderService(_projects, new SegmentRenderService(new StickerFitService(_renderer), _renderer, Substitute.For<IMediaProbe>()));
        _renderer.DraftAsync(Arg.Any<StickerProbe>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new DraftSticker(call.Arg<string>(), 120_000, 100_000));
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private void Holds(params Segment[] segments) => _projects.ReadAsync(ProjectPath, Arg.Any<CancellationToken>()).Returns(new Project(
        Guid.NewGuid(), "Кот", 3, DateTimeOffset.UnixEpoch, new ProjectSource("D:/cat.gif", "cat.gif", "sha256:0", Scenes.Source), Scenes.Of(segments)));

    [Fact]
    public async Task Renders_each_segment_beside_the_project_and_tags_its_progress()
    {
        Holds(Scenes.Segment, Scenes.Segment with { Id = "seg-2" });
        var progress = new CollectProgress<SegmentProgress>();

        var outcomes = await _service.RenderAsync(ProjectPath, RenderEffort.Draft, progress, Cancel);

        outcomes.Select(outcome => outcome.Sticker!.Path).Should().Equal(
            Path.GetFullPath(Path.Combine("projects", "cat.seg-1.draft.webm")),
            Path.GetFullPath(Path.Combine("projects", "cat.seg-2.draft.webm")));
        progress.Reports.Select(report => report.SegmentId).Distinct().Should().Equal("seg-1", "seg-2");
    }

    [Fact]
    public async Task Goes_on_past_a_segment_the_engine_fails_on()
    {
        Holds(Scenes.Segment, Scenes.Segment with { Id = "seg-2" });
        _renderer.DraftAsync(Arg.Is<StickerProbe>(_ => true), Path.GetFullPath(Path.Combine("projects", "cat.seg-1.draft.webm")), Arg.Any<CancellationToken>())
            .Returns<DraftSticker>(_ => throw new AppException(ErrorCode.RenderFailed, "ffmpeg failed") { Log = "Invalid data" });

        var outcomes = await _service.RenderAsync(ProjectPath, RenderEffort.Draft, progress: null, Cancel);

        outcomes[0].Failure!.Code.Should().Be(ErrorCode.RenderFailed);
        outcomes[1].Sticker.Should().NotBeNull();
    }
}
