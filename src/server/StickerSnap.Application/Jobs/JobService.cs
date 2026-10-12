using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Jobs;

/// <summary>
/// /api/v1/jobs (contract, group jobs): checks a request against its project before the job is
/// queued, and again before «Повторить». An export and a publication go by the revision saved
/// last, so a stale one is revision_conflict; a preview's segment must keep the scene's rules.
/// </summary>
public sealed class JobService(JobQueue queue, ProjectService projects)
{
    /// <summary>POST /jobs: the job, queued or already running.</summary>
    /// <exception cref="AppException">
    /// project_not_found; revision_conflict; bad_request for segments the project does not have or a
    /// preview's segment that breaks a rule
    /// </exception>
    public async Task<Job> CreateAsync(JobRequest request, CancellationToken cancellationToken)
    {
        await CheckAsync(request, cancellationToken);
        return queue.Enqueue(request);
    }

    /// <summary>GET /jobs: running, waiting and finished in the last 10 minutes, newest first.</summary>
    public IReadOnlyList<Job> List() => queue.List();

    /// <exception cref="AppException">job_not_found</exception>
    public Job Get(string id) => queue.Get(id);

    /// <exception cref="AppException">job_not_found</exception>
    public Job Cancel(string id) => queue.Cancel(id);

    /// <summary>POST /jobs/{id}/retry: the segments left undone, if the project still is as the job saw it.</summary>
    /// <exception cref="AppException">job_not_found; bad_request for a job that succeeded; what <see cref="CreateAsync"/> checks</exception>
    public async Task<Job> RetryAsync(string id, CancellationToken cancellationToken)
    {
        if (queue.Get(id).CanRetry) await CheckAsync(queue.RequestOf(id), cancellationToken);
        return queue.Retry(id);
    }

    private async Task CheckAsync(JobRequest request, CancellationToken cancellationToken)
    {
        var project = (await projects.GetAsync(request.ProjectId, cancellationToken)).Project;
        switch (request)
        {
            case JobRequest.Preview preview:
                CheckSegment(preview.Segment, project);
                break;
            case JobRequest.Export export:
                CheckRevision(export.Revision, project);
                CheckSegmentIds(export.SegmentIds, "segmentIds", project);
                break;
            case JobRequest.Publish publish:
                CheckRevision(publish.Revision, project);
                CheckSegmentIds(publish.SegmentIds, "items", project);
                break;
        }
    }

    private static void CheckRevision(int revision, Project project)
    {
        if (revision != project.Revision)
            throw new AppException(
                ErrorCode.RevisionConflict,
                $"Project {project.Id} is at revision {project.Revision}, the job was asked for {revision}");
    }

    /// <summary>The segments of the job exist in the saved scene, each once.</summary>
    private static void CheckSegmentIds(IReadOnlyList<string> ids, string field, Project project)
    {
        var fields = new Dictionary<string, string[]>();
        if (ids.Count == 0) fields[field] = ["empty"];

        var saved = project.Scene.Segments.Select(segment => segment.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Count; i++)
        {
            if (!saved.Contains(ids[i])) fields[$"{field}[{i}]"] = ["unknown"];
            else if (!seen.Add(ids[i])) fields[$"{field}[{i}]"] = ["duplicate"];
        }

        if (fields.Count > 0)
            throw new AppException(ErrorCode.BadRequest, $"The job's segments do not match project {project.Id}")
            {
                Fields = fields,
            };
    }

    /// <summary>A preview's segment keeps the rules of a saved one: inside the source, a whole-pixel crop.</summary>
    private static void CheckSegment(Segment segment, Project project)
    {
        var alone = project.Scene with { Segments = [segment] };
        var problems = SceneValidator.Validate(alone, project.Source.Media);
        if (problems.Count == 0) return;

        throw new AppException(ErrorCode.BadRequest, $"The segment breaks {problems.Count} rule(s)")
        {
            Fields = problems
                .GroupBy(problem => problem.Field.Replace("segments[0]", "segment", StringComparison.Ordinal))
                .ToDictionary(group => group.Key, group => group.Select(problem => problem.Rule).ToArray()),
        };
    }
}
