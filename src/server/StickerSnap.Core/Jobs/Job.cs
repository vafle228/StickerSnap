using StickerSnap.Core.Errors;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;

namespace StickerSnap.Core.Jobs;

/// <summary>
/// A background job (contract, Job): a preview, an export or a publication of some segments of
/// one project, with its progress and how it ended. A snapshot: every move gives a new one.
/// The moves are <c>queued → running → succeeded | partial | failed | canceled</c>, a queued job
/// may be canceled before it runs, and a job that did not succeed may be queued again («Повторить»);
/// any other move throws.
/// </summary>
/// <param name="Items">One per segment, in the order asked for; never empty</param>
/// <param name="Pack">For a publication: the pack the stickers go to</param>
/// <param name="Error">Why it failed; for a partial job, why the first segment failed</param>
public sealed record Job(
    string Id,
    JobKind Kind,
    Guid ProjectId,
    JobState State,
    DateTimeOffset CreatedAt,
    JobProgress Progress,
    IReadOnlyList<JobItem> Items,
    string? Pack = null,
    JobError? Error = null)
{
    /// <summary>A new job waiting for its turn, a segment each in <paramref name="segmentIds"/>.</summary>
    public static Job Queue(
        string id, JobKind kind, Guid projectId, IReadOnlyList<string> segmentIds, DateTimeOffset createdAt, string? pack = null)
    {
        if (segmentIds.Count == 0) throw new ArgumentException("A job needs at least one segment", nameof(segmentIds));
        return new Job(
            id,
            kind,
            projectId,
            JobState.Queued,
            createdAt,
            JobProgress.None,
            [.. segmentIds.Select(segmentId => new JobItem(segmentId, JobItemState.Queued, 0))],
            pack);
    }

    /// <summary>Succeeded, partial, failed or canceled: it will not move unless it is retried.</summary>
    public bool IsOver => State is JobState.Succeeded or JobState.Partial or JobState.Failed or JobState.Canceled;

    /// <summary>«Повторить»: a job that did not succeed may run again for the segments it left undone.</summary>
    public bool CanRetry => State is JobState.Partial or JobState.Failed or JobState.Canceled;

    /// <summary>queued → running.</summary>
    public Job Start()
    {
        Ensure(State == JobState.Queued, "start");
        return this with { State = JobState.Running };
    }

    /// <summary>A segment of a running job moved on; the job's share is the mean of its segments'.</summary>
    /// <exception cref="ArgumentException">The job has no such segment</exception>
    public Job WithItem(string segmentId, JobItemState state, double fraction, JobError? error = null) =>
        Put(new JobItem(segmentId, state, state == JobItemState.Done ? 1 : Math.Clamp(fraction, 0, 1), error));

    /// <summary>
    /// A segment of a running job is done, with what it made: a preview's draft, an export's file,
    /// a publication's sticker in its pack.
    /// </summary>
    /// <exception cref="ArgumentException">The job has no such segment</exception>
    public Job WithDone(string segmentId, PreviewFile? preview = null, SegmentExport? export = null, StickerRef? sticker = null) =>
        Put(new JobItem(segmentId, JobItemState.Done, 1, Preview: preview, Export: export, Sticker: sticker));

    /// <summary>What a running job is busy with now; its share stays the segments'.</summary>
    public Job WithStage(JobStage stage, JobAttempt? attempt = null, DateTimeOffset? retryAt = null)
    {
        Ensure(State == JobState.Running, "report a stage");
        return this with { Progress = Progress with { Stage = stage, Attempt = attempt, RetryAt = retryAt } };
    }

    /// <summary>
    /// running → succeeded when every segment is done, partial when some are, failed when none is.
    /// Segments left in the middle fail with <paramref name="error"/>, those not begun are skipped.
    /// </summary>
    /// <param name="error">Why the job stopped, when it did not run to its end</param>
    public Job Finish(JobError? error = null)
    {
        Ensure(State == JobState.Running, "finish");
        var items = Items.Select(item => item switch
        {
            { IsOver: true } => item,
            { State: JobItemState.Queued } => item with { State = JobItemState.Skipped },
            _ => item with { State = JobItemState.Failed, Error = error ?? item.Error },
        }).ToList();

        var done = items.Count(item => item.State == JobItemState.Done);
        var state = done == items.Count ? JobState.Succeeded : done > 0 ? JobState.Partial : JobState.Failed;
        var firstError = error ?? items.Select(item => item.Error).FirstOrDefault(found => found is not null);
        if (state == JobState.Failed)
            firstError ??= JobError.Of(ErrorCode.Internal, $"Job {Id} ended with no segment done");

        return this with
        {
            State = state,
            Items = items,
            Progress = Progress with { Fraction = state == JobState.Succeeded ? 1 : MeanOf(items), RetryAt = null },
            Error = state == JobState.Succeeded ? null : firstError,
        };
    }

    /// <summary>queued or running → canceled; segments not over are skipped, those done stay done.</summary>
    public Job Cancel()
    {
        Ensure(State is JobState.Queued or JobState.Running, "be canceled");
        return this with
        {
            State = JobState.Canceled,
            Items = [.. Items.Select(item => item.IsOver ? item : item with { State = JobItemState.Skipped })],
            Progress = Progress with { RetryAt = null },
        };
    }

    /// <summary>
    /// «Повторить»: partial, failed or canceled → queued. Segments done stay done, the others wait
    /// again from the start, so a publication sends only what did not go.
    /// </summary>
    public Job Retry()
    {
        Ensure(CanRetry, "be retried");
        var items = Items
            .Select(item => item.State == JobItemState.Done ? item : new JobItem(item.SegmentId, JobItemState.Queued, 0))
            .ToList();
        return this with
        {
            State = JobState.Queued,
            Items = items,
            Progress = JobProgress.None with { Fraction = MeanOf(items) },
            Error = null,
        };
    }

    private Job Put(JobItem item)
    {
        Ensure(State == JobState.Running, "report a segment");
        var items = Items.ToArray();
        items[IndexOf(item.SegmentId)] = item;
        return this with { Items = items, Progress = Progress with { Fraction = MeanOf(items) } };
    }

    private int IndexOf(string segmentId)
    {
        for (var i = 0; i < Items.Count; i++)
            if (Items[i].SegmentId == segmentId) return i;
        throw new ArgumentException($"Job {Id} has no segment {segmentId}", nameof(segmentId));
    }

    private static double MeanOf(IReadOnlyList<JobItem> items) => items.Average(item => item.Fraction);

    private void Ensure(bool allowed, string move)
    {
        if (!allowed) throw new InvalidOperationException($"A {State.ToString().ToLowerInvariant()} job cannot {move}");
    }
}
