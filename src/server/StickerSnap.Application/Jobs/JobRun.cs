using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Jobs;

/// <summary>
/// One run of a job, as its <see cref="IJobExecutor"/> sees it: what was asked, and where it
/// reports how the segments go. Reports after the job was canceled are dropped.
/// </summary>
public sealed class JobRun
{
    private readonly JobQueue _queue;

    /// <summary>Ends a wait of <see cref="WaitAsync"/> early; null while the run does not wait.</summary>
    private TaskCompletionSource? _wake;

    internal JobRun(JobQueue queue, JobQueue.Entry entry)
    {
        _queue = queue;
        Entry = entry;
        Request = entry.Request;
    }

    internal JobQueue.Entry Entry { get; }

    public JobRequest Request { get; }

    /// <summary>The job as it is now.</summary>
    public Job Job => _queue.SnapshotOf(this);

    /// <summary>The segments not yet done: all of them, or after «Повторить» those that did not go.</summary>
    public IReadOnlyList<string> SegmentsToDo =>
        [.. Job.Items.Where(item => item.State != JobItemState.Done).Select(item => item.SegmentId)];

    /// <summary>A segment moved on: its state and its own share 0..1.</summary>
    public void Item(string segmentId, JobItemState state, double fraction) =>
        _queue.Report(this, job => job.WithItem(segmentId, state, fraction));

    /// <summary>A segment is done, with what it made: a preview's draft, an export's file, a sent sticker.</summary>
    public void Done(string segmentId, PreviewFile? preview = null, SegmentExport? export = null, StickerRef? sticker = null) =>
        _queue.Report(this, job => job.WithDone(segmentId, preview, export, sticker));

    /// <summary>A segment failed; the job goes on with the others and ends partial.</summary>
    public void ItemFailed(string segmentId, AppException error) =>
        _queue.Report(this, job => job.WithItem(
            segmentId,
            JobItemState.Failed,
            job.Items.FirstOrDefault(item => item.SegmentId == segmentId)?.Fraction ?? 0,
            JobError.Of(error)));

    /// <summary>
    /// A segment is left out, for <paramref name="why"/>: a sticker that did not pass the checks is
    /// not sent (contract, 7-3); the job goes on with the others. «Повторить» tries it again.
    /// </summary>
    public void ItemSkipped(string segmentId, AppException why) =>
        _queue.Report(this, job => job.WithItem(
            segmentId,
            JobItemState.Skipped,
            job.Items.FirstOrDefault(item => item.SegmentId == segmentId)?.Fraction ?? 0,
            JobError.Of(why)));

    /// <summary>
    /// Telegram asked to wait (429): the job shows the wait and when it goes on by itself, and
    /// returns at <paramref name="retryAt"/>, or at once when the user presses «Повторить сейчас».
    /// </summary>
    /// <exception cref="OperationCanceledException">The job was canceled while it waited</exception>
    public async Task WaitAsync(DateTimeOffset retryAt, CancellationToken cancellationToken)
    {
        var wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _wake, wake);
        Stage(JobStage.Wait, retryAt: retryAt);
        try
        {
            var delay = retryAt - _queue.Clock.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var sleeping = Task.Delay(delay, _queue.Clock, timer.Token);
                await Task.WhenAny(sleeping, wake.Task);
                await timer.CancelAsync();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            Interlocked.CompareExchange(ref _wake, null, wake);
        }
    }

    /// <summary>«Повторить сейчас»: a wait of <see cref="WaitAsync"/> ends now; nothing happens when the run does not wait.</summary>
    internal void WakeUp() => Volatile.Read(ref _wake)?.TrySetResult();

    /// <summary>What the job is busy with: a pass, a step of the ladder, a wait Telegram asked for.</summary>
    public void Stage(JobStage stage, JobAttempt? attempt = null, DateTimeOffset? retryAt = null) =>
        _queue.Report(this, job => job.WithStage(stage, attempt, retryAt));
}
