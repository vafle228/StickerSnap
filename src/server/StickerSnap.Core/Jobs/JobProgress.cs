using StickerSnap.Core.Render;

namespace StickerSnap.Core.Jobs;

/// <summary>How far a job has got (contract, Job.progress).</summary>
/// <param name="Fraction">0..1 of the whole job, over all its segments</param>
/// <param name="Attempt">The step of the fit ladder being tried: «попытка 2, 24 fps»</param>
/// <param name="RetryAt">For <see cref="JobStage.Wait"/>: when the job goes on by itself</param>
public sealed record JobProgress(double Fraction, JobStage Stage, JobAttempt? Attempt = null, DateTimeOffset? RetryAt = null)
{
    /// <summary>A job that has not started.</summary>
    public static JobProgress None { get; } = new(0, JobStage.Probe);
}

/// <summary>A step of the fit ladder: its place from 1 and what it gives up, none for the first.</summary>
public sealed record JobAttempt(int N, IReadOnlyList<Concession> Concessions);
