using StickerSnap.Core.Jobs;

namespace StickerSnap.Application.Jobs;

/// <summary>
/// The background jobs of the app: queued in lanes, run by the <see cref="IJobExecutor"/> of their
/// kind, cancelled and retried, each change sent as jobUpdated. Takes requests already checked
/// against their project (<see cref="IJobService"/> does that).
/// </summary>
public interface IJobQueue
{
    /// <summary>A new job for <paramref name="request"/>, queued in its lane, or already running when the lane was free.</summary>
    Job Enqueue(JobRequest request);

    /// <summary>The jobs running, waiting and finished in the last 10 minutes, newest first.</summary>
    IReadOnlyList<Job> List();

    /// <exception cref="Core.Errors.AppException">job_not_found</exception>
    Job Get(string id);

    /// <summary>What the job was asked to do, to check it again before «Повторить».</summary>
    /// <exception cref="Core.Errors.AppException">job_not_found</exception>
    JobRequest RequestOf(string id);

    /// <summary>Cancels a queued or running job at once; a finished job is answered as it is.</summary>
    /// <exception cref="Core.Errors.AppException">job_not_found</exception>
    Job Cancel(string id);

    /// <summary>
    /// «Повторить»: a partial, failed or canceled job waits in its lane again for the segments it
    /// left undone; a job queued or running is answered as it is, and one waiting for Telegram
    /// (stage wait) goes on at once («Повторить сейчас»).
    /// </summary>
    /// <exception cref="Core.Errors.AppException">job_not_found; bad_request for a job that succeeded</exception>
    Job Retry(string id);
}
