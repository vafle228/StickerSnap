using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Jobs;

/// <summary>
/// One run of a job, as its <see cref="IJobExecutor"/> sees it: what was asked, and where it
/// reports how the segments go. Reports after the job was canceled are dropped.
/// </summary>
public sealed class JobRun
{
    private readonly JobQueue _queue;

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

    /// <summary>A segment is done, with what it made: a preview's draft or an export's sticker.</summary>
    public void Done(string segmentId, PreviewFile? preview = null, SegmentExport? export = null) =>
        _queue.Report(this, job => job.WithDone(segmentId, preview, export));

    /// <summary>A segment failed; the job goes on with the others and ends partial.</summary>
    public void ItemFailed(string segmentId, AppException error) =>
        _queue.Report(this, job => job.WithItem(
            segmentId,
            JobItemState.Failed,
            job.Items.FirstOrDefault(item => item.SegmentId == segmentId)?.Fraction ?? 0,
            JobError.Of(error)));

    /// <summary>What the job is busy with: a pass, a step of the ladder, a wait Telegram asked for.</summary>
    public void Stage(JobStage stage, JobAttempt? attempt = null, DateTimeOffset? retryAt = null) =>
        _queue.Report(this, job => job.WithStage(stage, attempt, retryAt));
}
