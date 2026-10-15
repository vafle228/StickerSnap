using System.Text.Json.Serialization;
using StickerSnap.Api.Json;
using StickerSnap.Api.Problems;
using StickerSnap.Application.App;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Packs;
using Domain = StickerSnap.Core.Jobs;
using Requests = StickerSnap.Application.Jobs;

namespace StickerSnap.Api.Contracts;

/// <summary>POST /api/v1/jobs (schema JobRequest): told apart by `kind`.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PreviewJobRequest), "preview")]
[JsonDerivedType(typeof(ExportJobRequest), "export")]
[JsonDerivedType(typeof(PublishJobRequest), "publish")]
public abstract record JobRequest(Guid ProjectId)
{
    public abstract Requests.JobRequest ToRequest();
}

/// <summary>3c D: a draft of the segment as the editor has it now.</summary>
public sealed record PreviewJobRequest(Guid ProjectId, Segment Segment) : JobRequest(ProjectId)
{
    public override Requests.JobRequest ToRequest() => new Requests.JobRequest.Preview(ProjectId, Segment.ToSegment());
}

/// <summary>By the snapshot of the saved revision.</summary>
public sealed record ExportJobRequest(Guid ProjectId, int Revision, IReadOnlyList<string> SegmentIds) : JobRequest(ProjectId)
{
    public override Requests.JobRequest ToRequest() => new Requests.JobRequest.Export(ProjectId, Revision, [.. SegmentIds]);
}

public sealed record PublishJobRequest(Guid ProjectId, int Revision, IReadOnlyList<PublishItem> Items, PublishTarget Target)
    : JobRequest(ProjectId)
{
    public override Requests.JobRequest ToRequest() => new Requests.JobRequest.Publish(
        ProjectId,
        Revision,
        [.. Items.Select(item => new Requests.PublishItem(item.SegmentId, [.. item.Emoji], item.Replace?.ToRef()))],
        Target.ToTarget());
}

/// <param name="Replace">The sticker it takes the place of</param>
public sealed record PublishItem(string SegmentId, IReadOnlyList<string> Emoji, StickerRef? Replace);

/// <summary>A sticker in a pack (schema StickerRef).</summary>
public sealed record StickerRef(string Pack, string StickerId)
{
    public Core.Packs.StickerRef ToRef() => new(Pack, StickerId);

    public static StickerRef From(Core.Packs.StickerRef sticker) => new(sticker.Pack, sticker.StickerId);
}

/// <summary>Where a publication goes, told apart by `kind`.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExistingPackTarget), "existing")]
[JsonDerivedType(typeof(NewPackTarget), "new")]
public abstract record PublishTarget
{
    public abstract Requests.PublishTarget ToTarget();
}

public sealed record ExistingPackTarget(string Pack) : PublishTarget
{
    public override Requests.PublishTarget ToTarget() => new Requests.PublishTarget.Existing(Pack);
}

public sealed record NewPackTarget(string Title, string Name, PackKind Type) : PublishTarget
{
    public override Requests.PublishTarget ToTarget() => new Requests.PublishTarget.New(Title, Name, Type);
}

/// <summary>A background job (schema Job): the answers of /api/v1/jobs and the event jobUpdated.</summary>
/// <param name="Pack">For publish: where it goes</param>
public sealed record Job(
    string Id,
    JobKind Kind,
    Guid ProjectId,
    JobState State,
    DateTimeOffset CreatedAt,
    JobProgress Progress,
    IReadOnlyList<JobItem> Items,
    string? Pack,
    ProblemBody? Error)
{
    /// <param name="mode">An export's path is the desktop's</param>
    public static Job From(Domain.Job job, AppMode mode) => new(
        job.Id,
        job.Kind,
        job.ProjectId,
        job.State,
        job.CreatedAt,
        JobProgress.From(job.Progress),
        [.. job.Items.Select(item => JobItem.From(item, job.Id, mode))],
        job.Pack,
        ProblemBody.From(job.Error, job.Id));
}

/// <param name="Fraction">0..1 of the whole job</param>
/// <param name="Attempt">«попытка 2, 24 fps»</param>
/// <param name="RetryAt">429 from Telegram: «повторим сами через 0:24»</param>
public sealed record JobProgress(double Fraction, JobStage Stage, JobAttempt? Attempt, DateTimeOffset? RetryAt)
{
    public static JobProgress From(Domain.JobProgress progress) => new(
        progress.Fraction,
        progress.Stage,
        progress.Attempt is { } attempt ? new JobAttempt(attempt.N, [.. attempt.Concessions.Select(Concession.Of)]) : null,
        progress.RetryAt);
}

public sealed record JobAttempt(int N, IReadOnlyList<Concession> Concessions);

/// <summary>One segment of a job (schema JobItem).</summary>
/// <param name="Preview">preview: «Точное · 186 КБ»</param>
public sealed record JobItem(
    string SegmentId,
    JobItemState State,
    double Fraction,
    JobItemPreview? Preview,
    ExportResult? Export,
    StickerRef? Sticker,
    ProblemBody? Error)
{
    /// <summary>An export just made is of the segment as it was saved: not stale.</summary>
    public static JobItem From(Domain.JobItem item, string jobId, AppMode mode) => new(
        item.SegmentId,
        item.State,
        item.Fraction,
        item.Preview is { } preview ? new JobItemPreview(FileUrls.Of(preview.FileId), preview.Bytes) : null,
        item.Export is { } export ? ExportResult.From(export, stale: false, mode) : null,
        item.Sticker is { } sticker ? StickerRef.From(sticker) : null,
        ProblemBody.From(item.Error, jobId));
}

/// <param name="FileUrl">GET /api/v1/files/{fileId}</param>
public sealed record JobItemPreview(string FileUrl, long Bytes);

/// <summary>
/// The failure of a job or of one of its segments as the schema Problem has it, the same body an
/// answer's problem carries. Its trace id is the job's, which the server's log names.
/// </summary>
public sealed record ProblemBody(
    string Type,
    int Status,
    string Title,
    string? Detail,
    ErrorCode Code,
    [property: StringOrNumberValues] IReadOnlyDictionary<string, object>? Params,
    IReadOnlyDictionary<string, string[]>? Fields,
    string? Log,
    string TraceId)
{
    public static ProblemBody? From(JobError? error, string traceId)
    {
        if (error is null) return null;
        var problem = Problem.Of(error.Code, error.Message);
        return new ProblemBody(
            problem.Type!,
            problem.Status!.Value,
            problem.Title!,
            problem.Detail,
            error.Code,
            error.Params.Count > 0 ? error.Params : null,
            error.Fields.Count > 0 ? error.Fields : null,
            error.Log,
            traceId);
    }
}
