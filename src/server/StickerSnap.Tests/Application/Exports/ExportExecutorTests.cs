using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Exports;
using StickerSnap.Application.Files;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Render;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Settings;
using StickerSnap.Tests.Application.Jobs;
using StickerSnap.Tests.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Exports;

/// <summary>
/// «Экспорт WEBM» run by the queue over a render engine and a stickers folder that only pretend:
/// the order a segment goes through, where its file goes, what the project keeps, and how a
/// failure, a stale revision and a cancel end the job.
/// </summary>
public sealed class ExportExecutorTests : IAsyncDisposable
{
    private static readonly string Stickers = Path.Combine(Path.GetTempPath(), "Стикеры");
    private static readonly Project Cat = SampleProjects.Sample;

    private readonly IProjectService _projects = Substitute.For<IProjectService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ISegmentRenderService _engine = Substitute.For<ISegmentRenderService>();
    private readonly IStickerFolder _folder = Substitute.For<IStickerFolder>();
    private readonly IRenderedFiles _files = Substitute.For<IRenderedFiles>();
    private readonly RecordingPublisher _events = new();
    private readonly List<SegmentRender> _renders = [];
    private readonly List<StickerSlot> _slots = [];
    private readonly JobQueue _queue;
    private int _fileIds;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ExportExecutorTests()
    {
        Opens(Cat);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(UserSettings.Default(new Folders("/projects", Stickers)));
        _engine.RenderAsync(Arg.Any<SegmentRender>(), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<SegmentRender>();
                lock (_renders) _renders.Add(request);
                Renders.ReportStages(call.Arg<IProgress<RenderProgress>?>());
                return Renders.Of(request);
            });
        _folder.ReserveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var slot = new StickerSlot(call.Arg<string>(), Path.Combine(call.Arg<string>(), $".{_slots.Count}.part.webm"));
            _slots.Add(slot);
            return slot;
        });
        _folder.KeepAsync(Arg.Any<StickerSlot>(), Arg.Any<IEnumerable<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Path.Combine(call.Arg<StickerSlot>().Folder, call.Arg<IEnumerable<string>>().First()));
        _files.AddAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => $"file-{Interlocked.Increment(ref _fileIds)}");

        var executor = new ExportExecutor(_projects, _settings, _engine, _folder, _files);
        _queue = new JobQueue([executor], _events, new FakeTimeProvider(), NullLogger<JobQueue>.Instance);
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private void Opens(Project project, PresenceStatus source = PresenceStatus.Ok) =>
        _projects.GetAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(new OpenedProject(project, "/projects/cat.ssproj", source));

    private async Task<Job> ExportAsync(JobState end, int revision = 12, params string[] segments)
    {
        var job = _queue.Enqueue(new JobRequest.Export(Cat.Id, revision, segments.Length > 0 ? segments : ["seg-1", "seg-2"]));
        return await _events.UntilAsync(job.Id, end);
    }

    [Fact]
    public async Task Exports_the_segments_one_after_another_through_their_states()
    {
        var seen = new List<string>();
        _engine.RenderAsync(Arg.Any<SegmentRender>(), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<SegmentRender>();
                string States() => string.Join(" ", _queue.List().Single().Items.Select(item => item.State.ToString().ToLowerInvariant()));
                seen.Add($"{request.SegmentId} starts: {States()}");
                Renders.ReportStages(call.Arg<IProgress<RenderProgress>?>());
                seen.Add($"{request.SegmentId} checks: {States()}, stage {_queue.List().Single().Progress.Stage}");
                return Renders.Of(request);
            });

        var done = await ExportAsync(JobState.Succeeded);

        seen.Should().Equal(
            "seg-1 starts: exporting queued",
            "seg-1 checks: validating queued, stage Validate",
            "seg-2 starts: done exporting",
            "seg-2 checks: done validating, stage Validate");
        done.Items.Should().OnlyContain(item => item.State == JobItemState.Done && item.Export != null);
        done.Progress.Fraction.Should().Be(1);
    }

    [Fact]
    public async Task Reports_the_step_of_the_ladder_being_tried()
    {
        JobProgress? during = null;
        _engine.RenderAsync(Arg.Any<SegmentRender>(), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<RenderProgress>?>()!.Report(
                    new RenderProgress(0.4, RenderStage.Pass1, new RenderAttempt(2, SampleExports.Second.Concessions)));
                during = _queue.List().Single().Progress;
                return Renders.Of(call.Arg<SegmentRender>());
            });

        await ExportAsync(JobState.Succeeded, segments: "seg-1");

        during!.Stage.Should().Be(JobStage.Pass1);
        during.Attempt.Should().BeEquivalentTo(new JobAttempt(2, [new Concession.Fps(30, 24)]));
        during.Fraction.Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public async Task Renders_the_saved_scene_finally_into_the_stickers_folder_named_after_project_and_segment()
    {
        await ExportAsync(JobState.Succeeded);

        _renders.Should().HaveCount(2);
        _renders[0].Should().BeEquivalentTo(new
        {
            SourcePath = Cat.Source.Path,
            SegmentId = "seg-1",
            Quality = RenderEffort.Final,
            OutputPath = _slots[0].TemporaryPath,
        });
        _renders[0].Scene.Should().BeSameAs(Cat.Scene);
        await _folder.Received(1).KeepAsync(
            _slots[0],
            Arg.Is<IEnumerable<string>>(names => names.First() == "Кот и клавиатура — Стикер 1.webm"),
            null,
            Arg.Any<CancellationToken>());
        _slots.Should().OnlyContain(slot => slot.Folder == Stickers);
    }

    [Fact]
    public async Task Records_each_export_in_the_project_by_the_revision_asked_for()
    {
        var done = await ExportAsync(JobState.Succeeded);

        var export = done.Items[0].Export!;
        export.Should().BeEquivalentTo(new
        {
            SegmentId = "seg-1",
            SceneRevision = 12,
            Signature = SegmentSignature.Of(Cat.Scene.Segments[0], Cat.Source),
            FileId = "file-1",
            Path = Path.Combine(Stickers, "Кот и клавиатура — Стикер 1.webm"),
            Bytes = 190_464L,
            Crf = 40,
        });
        export.Validation.Should().Be(SampleExports.Green);
        export.Ladder.Select(step => step.Bytes).Should().Equal(300_000, 190_464);
        export.IsStaleIn(Cat).Should().BeFalse();
        await _projects.Received(1).RecordExportAsync(Cat.Id, export, Arg.Any<CancellationToken>());
        await _projects.Received(1).RecordExportAsync(Cat.Id, done.Items[1].Export!, Arg.Any<CancellationToken>());
        await _files.Received(1).AddAsync(export.Path, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replaces_the_segment_s_last_export_and_forgets_its_file_id()
    {
        var last = SampleExports.Of(Cat.Scene.Segments[0], Cat.Source, fileId: "old") with
        {
            Path = Path.Combine(Stickers, "Кот и клавиатура — Стикер 1 (2).webm"),
        };
        Opens(Cat.WithExport(last));

        await ExportAsync(JobState.Succeeded, segments: "seg-1");

        await _folder.Received(1).KeepAsync(Arg.Any<StickerSlot>(), Arg.Any<IEnumerable<string>>(), last.Path, Arg.Any<CancellationToken>());
        await _files.Received(1).RemoveAsync("old", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Goes_on_with_the_others_when_a_segment_fails_and_ends_partial()
    {
        _engine.RenderAsync(Arg.Is<SegmentRender>(render => render.SegmentId == "seg-1"), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns<RenderedSegment>(_ => throw new AppException(ErrorCode.CannotFit, "The last step is 300000 bytes") { Log = "300000 > 262144" });

        var partial = await ExportAsync(JobState.Partial);

        partial.Items.Select(item => item.State).Should().Equal(JobItemState.Failed, JobItemState.Done);
        partial.Items[0].Error!.Code.Should().Be(ErrorCode.CannotFit);
        partial.Items[0].Error!.Log.Should().Be("300000 > 262144");
        partial.Error!.Code.Should().Be(ErrorCode.CannotFit);
        _folder.Received(1).Discard(_slots[0]);
        await _projects.Received(1).RecordExportAsync(Cat.Id, Arg.Is<SegmentExport>(export => export.SegmentId == "seg-2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fails_with_disk_full_when_the_stickers_folder_cannot_take_the_file()
    {
        _folder.KeepAsync(Arg.Any<StickerSlot>(), Arg.Any<IEnumerable<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new AppException(ErrorCode.DiskFull, "No space left"));

        var failed = await ExportAsync(JobState.Failed, segments: "seg-1");

        failed.Error!.Code.Should().Be(ErrorCode.DiskFull);
        _folder.Received(1).Discard(_slots[0]);
        await _projects.DidNotReceive().RecordExportAsync(Arg.Any<Guid>(), Arg.Any<SegmentExport>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fails_with_revision_conflict_when_the_scene_was_saved_since()
    {
        var failed = await ExportAsync(JobState.Failed, revision: 11);

        failed.Error!.Code.Should().Be(ErrorCode.RevisionConflict);
        failed.Items.Should().OnlyContain(item => item.State == JobItemState.Skipped);
        _renders.Should().BeEmpty();
    }

    [Fact]
    public async Task Fails_with_source_not_found_when_the_video_is_gone()
    {
        Opens(Cat, PresenceStatus.Missing);

        var failed = await ExportAsync(JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.SourceNotFound);
        _renders.Should().BeEmpty();
    }

    [Fact]
    public async Task Stops_a_render_when_canceled_and_leaves_nothing_behind()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.RenderAsync(Arg.Any<SegmentRender>(), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return Renders.Of(call.Arg<SegmentRender>());
            });
        var job = _queue.Enqueue(new JobRequest.Export(Cat.Id, 12, ["seg-1", "seg-2"]));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        _queue.Cancel(job.Id);
        await _queue.DisposeAsync();

        _queue.Get(job.Id).State.Should().Be(JobState.Canceled);
        _folder.Received(1).Discard(_slots[0]);
        _slots.Should().HaveCount(1);
        await _projects.DidNotReceive().RecordExportAsync(Arg.Any<Guid>(), Arg.Any<SegmentExport>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exports_after_a_retry_only_the_segments_that_did_not_go()
    {
        _engine.RenderAsync(Arg.Is<SegmentRender>(render => render.SegmentId == "seg-2"), Arg.Any<IProgress<RenderProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new AppException(ErrorCode.RenderFailed, "The engine stopped"),
                call =>
                {
                    lock (_renders) _renders.Add(call.Arg<SegmentRender>());
                    return Renders.Of(call.Arg<SegmentRender>());
                });
        var partial = await ExportAsync(JobState.Partial);
        _renders.Clear();

        _queue.Retry(partial.Id);
        var done = await _events.UntilAsync(partial.Id, JobState.Succeeded);

        _renders.Select(render => render.SegmentId).Should().Equal("seg-2");
        done.Items[0].Export.Should().BeSameAs(partial.Items[0].Export);
    }
}
