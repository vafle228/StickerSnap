using StickerSnap.Core.Jobs;

namespace StickerSnap.Application.Jobs;

/// <summary>
/// Does the work of one kind of job for <see cref="IJobQueue"/>: renders a preview, exports, or
/// publishes. Registered in DI, one per <see cref="Kind"/>; a kind with none fails its jobs.
/// </summary>
public interface IJobExecutor
{
    JobKind Kind { get; }

    /// <summary>
    /// Works through <see cref="JobRun.SegmentsToDo"/>, telling the run how each goes. Returning
    /// ends the job by its segments; an <see cref="Core.Errors.AppException"/> ends it with that
    /// error. When <paramref name="cancellationToken"/> fires the job is already canceled: stop,
    /// clean up, and throw <see cref="OperationCanceledException"/>.
    /// </summary>
    Task RunAsync(JobRun run, CancellationToken cancellationToken);
}
