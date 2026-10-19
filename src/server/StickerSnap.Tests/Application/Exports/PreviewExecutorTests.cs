using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Exports;
using StickerSnap.Application.Files;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Render;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Tests.Application.Jobs;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Exports;

/// <summary>
/// «Точное превью» run by the queue over a render engine that only pretends: the segment as the
/// editor has it becomes a draft among temporary files, and the job says what the sticker will weigh.
/// </summary>
public sealed class PreviewExecutorTests : IAsyncDisposable
{
    private static readonly Project Cat = SampleProjects.Sample;
    private static readonly RenderedFile Draft = new("0f8e1d2c3b4a59687766554433221100", "/data/drafts/0f8e1d2c3b4a59687766554433221100.webm");

    private readonly IProjectService _projects = Substitute.For<IProjectService>();
    private readonly ISegmentRenderService _engine = Substitute.For<ISegmentRenderService>();
    private readonly IRenderedFiles _files = Substitute.For<IRenderedFiles>();
    private readonly RecordingPublisher _events = new();
    private readonly JobQueue _queue;
    private SegmentRender? _render;

    public PreviewExecutorTests()
    {
        Opens(PresenceStatus.Ok);
        _files.NewDraft().Returns(Draft);
        _engine.RenderAsync(Arg.Any<SegmentRender>(), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _render = call.Arg<SegmentRender>();
                call.Arg<IProgress<RenderProgress>?>()?.Report(new RenderProgress(0.5, RenderStage.Pass2, null));
                return Renders.Of(_render);
            });

        var executor = new PreviewExecutor(_projects, _engine, _files);
        _queue = new JobQueue([executor], _events, Substitute.For<IErrorJournal>(), new FakeTimeProvider(), NullLogger<JobQueue>.Instance);
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private void Opens(PresenceStatus source) =>
        _projects.GetAsync(Cat.Id, Arg.Any<CancellationToken>()).Returns(new OpenedProject(Cat, "/projects/cat.ssproj", source));

    /// <summary>An edit not saved yet: the first segment, shorter and faster.</summary>
    private static readonly Segment Edited = Cat.Scene.Segments[0] with { OutFrame = 19, Speed = 2 };

    private async Task<Job> PreviewAsync(JobState end)
    {
        var job = _queue.Enqueue(new JobRequest.Preview(Cat.Id, Edited));
        return await _events.UntilAsync(job.Id, end);
    }

    [Fact]
    public async Task Renders_the_segment_as_the_editor_has_it_into_a_draft()
    {
        await PreviewAsync(JobState.Succeeded);

        _render!.Quality.Should().Be(RenderEffort.Draft);
        _render.OutputPath.Should().Be(Draft.Path);
        _render.SourcePath.Should().Be(Cat.Source.Path);
        _render.SegmentId.Should().Be("seg-1");
        _render.Scene.Segments.Should().Equal(Edited);
        _render.Scene.Layers.Should().BeEquivalentTo(Cat.Scene.Layers);
    }

    [Fact]
    public async Task Gives_the_draft_s_id_and_what_the_final_sticker_will_weigh()
    {
        var done = await PreviewAsync(JobState.Succeeded);

        done.Items.Should().ContainSingle().Which.Should().Be(
            new JobItem("seg-1", JobItemState.Done, 1, Preview: new PreviewFile(Draft.Id, 190_464)));
        await _projects.DidNotReceive().RecordExportAsync(Arg.Any<Guid>(), Arg.Any<SegmentExport>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fails_the_job_with_the_engine_s_code()
    {
        _engine.RenderAsync(Arg.Any<SegmentRender>(), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns<RenderedSegment>(_ => throw new AppException(ErrorCode.CannotFit, "The last step is 300000 bytes"));

        var failed = await PreviewAsync(JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.CannotFit);
        failed.Items.Single().State.Should().Be(JobItemState.Failed);
    }

    [Fact]
    public async Task Fails_with_source_not_found_when_the_video_is_gone()
    {
        Opens(PresenceStatus.Missing);

        var failed = await PreviewAsync(JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.SourceNotFound);
        _render.Should().BeNull();
    }
}
