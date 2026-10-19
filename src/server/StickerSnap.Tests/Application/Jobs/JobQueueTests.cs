using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Jobs;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Packs;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Application.Jobs;

/// <summary>
/// The queue with executors the test drives and a fake clock: the lanes, cancelling, retrying,
/// how long finished jobs stay, and how jobUpdated is thinned out.
/// </summary>
public sealed class JobQueueTests : IAsyncDisposable
{
    private static readonly Guid Cat = Guid.Parse("6f1c2a4e-8b3d-4f7a-9c21-5d0e7a3b9f12");
    private static readonly Guid Dog = Guid.Parse("0b6c1f2e-3d4a-4b5c-8d9e-0f1a2b3c4d5e");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingPublisher _events = new();
    private readonly ControlledExecutor _previews = new(JobKind.Preview);
    private readonly ControlledExecutor _exports = new(JobKind.Export);
    private readonly ControlledExecutor _publications = new(JobKind.Publish);
    private readonly ErrorJournal _journal = new(NullLogger<ErrorJournal>.Instance);
    private readonly JobQueue _queue;

    public JobQueueTests()
    {
        _queue = new JobQueue([_previews, _exports, _publications], _events, _journal, _clock, NullLogger<JobQueue>.Instance);
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private static JobRequest Preview(Guid project, string segment = "seg-1") =>
        new JobRequest.Preview(project, Scenes.Segment with { Id = segment });

    private static JobRequest Export(Guid project, params string[] segments) =>
        new JobRequest.Export(project, 12, segments.Length > 0 ? segments : ["seg-1", "seg-2"]);

    private static JobRequest Publish(Guid project) => new JobRequest.Publish(
        project, 12, [new PublishItem("seg-1", ["🐱"], null)], new PublishTarget.Existing("cats_by_StickerSnapAppBot"));

    [Fact]
    public async Task Runs_a_job_at_once_in_a_free_lane_and_ends_it_by_its_segments()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();

        call.Complete();
        var done = await _events.UntilAsync(job.Id, JobState.Succeeded);

        job.State.Should().Be(JobState.Running);
        job.Items.Select(item => item.SegmentId).Should().Equal("seg-1", "seg-2");
        call.Run.Request.Kind.Should().Be(JobKind.Export);
        done.Progress.Fraction.Should().Be(1);
        _queue.Get(job.Id).Should().Be(done);
    }

    [Fact]
    public void Takes_the_pack_of_a_publication_to_an_existing_pack()
    {
        var job = _queue.Enqueue(Publish(Cat));

        job.Pack.Should().Be("cats_by_StickerSnapAppBot");
    }

    [Fact]
    public async Task Queues_a_second_publication_behind_the_first_and_starts_it_when_the_first_ends()
    {
        var first = _queue.Enqueue(Publish(Cat));
        var running = await _publications.NextAsync();

        var second = _queue.Enqueue(Publish(Dog));
        second.State.Should().Be(JobState.Queued);
        _publications.HasWaitingCall.Should().BeFalse();

        running.Complete();
        await _events.UntilAsync(first.Id, JobState.Succeeded);
        var next = await _publications.NextAsync();

        next.Run.Job.Id.Should().Be(second.Id);
        _queue.Get(second.Id).State.Should().Be(JobState.Running);
    }

    [Fact]
    public async Task Puts_exports_and_publications_in_one_lane()
    {
        _queue.Enqueue(Export(Cat));
        await _exports.NextAsync();

        var publication = _queue.Enqueue(Publish(Cat));

        publication.State.Should().Be(JobState.Queued);
    }

    [Fact]
    public async Task Runs_a_preview_beside_an_export()
    {
        _queue.Enqueue(Export(Cat));
        await _exports.NextAsync();

        var preview = _queue.Enqueue(Preview(Cat));
        await _previews.NextAsync();

        preview.State.Should().Be(JobState.Running);
    }

    [Fact]
    public async Task Cancels_the_preview_of_the_same_project_when_a_new_one_comes()
    {
        var old = _queue.Enqueue(Preview(Cat));
        var oldCall = await _previews.NextAsync();

        var fresh = _queue.Enqueue(Preview(Cat, "seg-2"));

        _queue.Get(old.Id).State.Should().Be(JobState.Canceled);
        oldCall.Cancellation.IsCancellationRequested.Should().BeTrue();
        var freshCall = await _previews.NextAsync();
        freshCall.Run.Job.Id.Should().Be(fresh.Id);
        (await _events.UntilAsync(old.Id, JobState.Canceled)).Items.Single().State.Should().Be(JobItemState.Skipped);
    }

    [Fact]
    public async Task Lets_the_preview_of_another_project_wait_its_turn()
    {
        var cat = _queue.Enqueue(Preview(Cat));
        var catCall = await _previews.NextAsync();

        var dog = _queue.Enqueue(Preview(Dog));

        dog.State.Should().Be(JobState.Queued);
        _queue.Get(cat.Id).State.Should().Be(JobState.Running);
        catCall.Complete();
        (await _previews.NextAsync()).Run.Job.Id.Should().Be(dog.Id);
    }

    [Fact]
    public async Task Cancels_a_waiting_job_so_it_never_runs()
    {
        var first = _queue.Enqueue(Export(Cat));
        var running = await _exports.NextAsync();
        var waiting = _queue.Enqueue(Export(Dog));

        var canceled = _queue.Cancel(waiting.Id);
        running.Complete();
        await _events.UntilAsync(first.Id, JobState.Succeeded);

        canceled.State.Should().Be(JobState.Canceled);
        _exports.Runs.Should().Be(1);
        _exports.HasWaitingCall.Should().BeFalse();
    }

    [Fact]
    public async Task Cancels_a_running_job_at_once_keeping_what_is_done_and_frees_the_lane_when_the_executor_stops()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();
        call.Run.Item("seg-1", JobItemState.Done, 1);
        var next = _queue.Enqueue(Export(Dog));

        var canceled = _queue.Cancel(job.Id);
        call.Run.Item("seg-2", JobItemState.Exporting, 0.5);

        canceled.State.Should().Be(JobState.Canceled);
        canceled.Items.Select(item => item.State).Should().Equal(JobItemState.Done, JobItemState.Skipped);
        call.Cancellation.IsCancellationRequested.Should().BeTrue();
        _queue.Get(job.Id).Should().Be(canceled, "a report after the cancel is dropped");
        (await _exports.NextAsync()).Run.Job.Id.Should().Be(next.Id, "the lane goes on once the executor stopped");
    }

    [Fact]
    public async Task Answers_a_finished_job_as_it_is_when_canceled()
    {
        var job = _queue.Enqueue(Export(Cat));
        (await _exports.NextAsync()).Complete();
        var done = await _events.UntilAsync(job.Id, JobState.Succeeded);

        _queue.Cancel(job.Id).Should().Be(done);
    }

    [Fact]
    public async Task Fails_a_job_with_the_error_its_executor_throws()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();

        call.Throw(new AppException(ErrorCode.DiskFull, "No space left on device") { Log = "ENOSPC" });
        var failed = await _events.UntilAsync(job.Id, JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.DiskFull);
        failed.Error.Log.Should().Be("ENOSPC");
    }

    [Fact]
    public async Task Fails_as_internal_on_any_other_exception()
    {
        var job = _queue.Enqueue(Export(Cat));

        (await _exports.NextAsync()).Throw(new InvalidOperationException("A bug"));
        var failed = await _events.UntilAsync(job.Id, JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.Internal);
    }

    [Fact]
    public async Task Keeps_a_failure_for_a_report_under_the_job_s_id_with_its_project_and_command()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();

        call.Throw(new AppException(ErrorCode.RenderFailed, "ffmpeg failed pass 2")
        {
            Context = new Dictionary<string, string> { ["command"] = "ffmpeg -i cat.gif" },
        });
        await _events.UntilAsync(job.Id, JobState.Failed);

        var record = _journal.Find(job.Id).Should().ContainSingle().Which;
        record.Code.Should().Be(ErrorCode.RenderFailed);
        record.Operation.Should().Be("export job");
        record.At.Should().Be(_clock.GetUtcNow());
        record.Context.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            [ErrorRecord.ProjectKey] = Cat.ToString(),
            ["command"] = "ffmpeg -i cat.gif",
        });
    }

    [Fact]
    public async Task Keeps_the_failure_of_a_segment_with_the_segment_and_the_pack()
    {
        var job = _queue.Enqueue(Publish(Cat));
        var call = await _publications.NextAsync();

        call.Run.ItemFailed("seg-1", new AppException(ErrorCode.TelegramError, "Bad Request: STICKER_VIDEO_LONG"));
        call.Return();
        await _events.UntilAsync(job.Id, JobState.Failed);

        var record = _journal.Find(job.Id).Should().ContainSingle().Which;
        record.Context[ErrorRecord.SegmentKey].Should().Be("seg-1");
        record.Context[ErrorRecord.PackKey].Should().Be("cats_by_StickerSnapAppBot");
    }

    [Fact]
    public async Task Ends_partial_when_a_segment_failed_and_the_others_were_done()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();

        call.Run.ItemFailed("seg-1", new AppException(ErrorCode.CannotFit, "Smallest attempt 300 KB"));
        call.Run.Item("seg-2", JobItemState.Done, 1);
        call.Return();
        var partial = await _events.UntilAsync(job.Id, JobState.Partial);

        partial.Error!.Code.Should().Be(ErrorCode.CannotFit);
        partial.Items.Select(item => item.State).Should().Equal(JobItemState.Failed, JobItemState.Done);
    }

    [Fact]
    public async Task Fails_a_job_of_a_kind_nothing_runs()
    {
        await using var queue = new JobQueue([], _events, Substitute.For<IErrorJournal>(), _clock, NullLogger<JobQueue>.Instance);

        var job = queue.Enqueue(Export(Cat));
        var failed = await _events.UntilAsync(job.Id, JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.Internal);
    }

    [Fact]
    public async Task Runs_again_only_the_segments_left_undone_when_retried()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();
        call.Run.Item("seg-1", JobItemState.Done, 1);
        call.Throw(new AppException(ErrorCode.DiskFull, "No space left on device"));
        await _events.UntilAsync(job.Id, JobState.Partial);

        var retried = _queue.Retry(job.Id);
        var again = await _exports.NextAsync();

        retried.State.Should().Be(JobState.Running);
        retried.Error.Should().BeNull();
        again.Run.SegmentsToDo.Should().Equal("seg-2");
        again.Complete();
        (await _events.UntilAsync(job.Id, JobState.Succeeded)).Items.Should().OnlyContain(item => item.State == JobItemState.Done);
    }

    [Fact]
    public async Task Answers_a_running_job_as_it_is_when_retried_and_refuses_one_that_succeeded()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();

        _queue.Retry(job.Id).State.Should().Be(JobState.Running);
        call.Complete();
        await _events.UntilAsync(job.Id, JobState.Succeeded);
        var retry = () => _queue.Retry(job.Id);

        retry.Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.BadRequest);
    }

    [Fact]
    public void Says_job_not_found_for_a_job_it_does_not_have()
    {
        _queue.Invoking(queue => queue.Get("nope")).Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.JobNotFound);
        _queue.Invoking(queue => queue.Cancel("nope")).Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.JobNotFound);
        _queue.Invoking(queue => queue.Retry("nope")).Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.JobNotFound);
    }

    [Fact]
    public async Task Lists_the_newest_first_and_forgets_a_finished_job_after_ten_minutes()
    {
        var finished = _queue.Enqueue(Export(Cat));
        (await _exports.NextAsync()).Complete();
        await _events.UntilAsync(finished.Id, JobState.Succeeded);
        var running = _queue.Enqueue(Preview(Cat));
        await _previews.NextAsync();

        _clock.Advance(JobQueue.KeepFinished - TimeSpan.FromSeconds(1));
        var kept = _queue.List();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var later = _queue.List();

        kept.Select(job => job.Id).Should().Equal(running.Id, finished.Id);
        later.Select(job => job.Id).Should().Equal(running.Id);
        _queue.Invoking(queue => queue.Get(finished.Id)).Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.JobNotFound);
    }

    [Fact]
    public async Task Sends_at_most_one_event_a_job_per_interval_with_its_latest_snapshot()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();
        for (var i = 1; i <= 5; i++) call.Run.Item("seg-1", JobItemState.Exporting, i / 10.0);

        var held = _events.Sent(job.Id);
        _clock.Advance(JobQueue.EventInterval);
        var sent = _events.Sent(job.Id);

        held.Select(snapshot => snapshot.State).Should().Equal(JobState.Queued);
        sent.Should().HaveCount(2);
        sent[1].State.Should().Be(JobState.Running);
        sent[1].Items[0].Fraction.Should().Be(0.5);
    }

    [Fact]
    public async Task Sends_the_end_of_a_job_at_once_and_nothing_stale_after_it()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();
        call.Run.Item("seg-1", JobItemState.Exporting, 0.5);

        call.Complete();
        await _events.UntilAsync(job.Id, JobState.Succeeded);
        _clock.Advance(JobQueue.EventInterval);

        _events.Sent(job.Id).Select(snapshot => snapshot.State).Should().Equal(JobState.Queued, JobState.Succeeded);
    }

    [Fact]
    public async Task Sends_a_report_at_once_when_the_last_event_is_old_enough()
    {
        var job = _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();
        _clock.Advance(JobQueue.EventInterval);
        var afterRunning = _events.Sent(job.Id).Count;

        _clock.Advance(JobQueue.EventInterval);
        call.Run.Stage(JobStage.Pass1);

        afterRunning.Should().Be(2);
        _events.Sent(job.Id).Last().Progress.Stage.Should().Be(JobStage.Pass1);
    }

    [Fact]
    public async Task Stops_the_running_jobs_when_the_app_closes()
    {
        _queue.Enqueue(Export(Cat));
        var call = await _exports.NextAsync();

        await _queue.DisposeAsync();

        call.Cancellation.IsCancellationRequested.Should().BeTrue();
    }
}
