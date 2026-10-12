using StickerSnap.Core.Jobs;

namespace StickerSnap.Application.Jobs;

/// <summary>
/// /api/v1/jobs (contract, group jobs): a request is checked against its project before the job is
/// queued, and again before «Повторить».
/// </summary>
public interface IJobService
{
    /// <summary>POST /jobs: the job, queued or already running.</summary>
    /// <exception cref="Core.Errors.AppException">
    /// project_not_found; revision_conflict; bad_request for segments the project does not have or a
    /// preview's segment that breaks a rule
    /// </exception>
    Task<Job> CreateAsync(JobRequest request, CancellationToken cancellationToken);

    /// <summary>GET /jobs: running, waiting and finished in the last 10 minutes, newest first.</summary>
    IReadOnlyList<Job> List();

    /// <exception cref="Core.Errors.AppException">job_not_found</exception>
    Job Get(string id);

    /// <exception cref="Core.Errors.AppException">job_not_found</exception>
    Job Cancel(string id);

    /// <summary>POST /jobs/{id}/retry: the segments left undone, if the project still is as the job saw it.</summary>
    /// <exception cref="Core.Errors.AppException">job_not_found; bad_request for a job that succeeded; what <see cref="CreateAsync"/> checks</exception>
    Task<Job> RetryAsync(string id, CancellationToken cancellationToken);
}
