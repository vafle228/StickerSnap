using StickerSnap.Application.Jobs;
using StickerSnap.Application.Render;
using StickerSnap.Core.Jobs;

namespace StickerSnap.Application.Exports;

/// <summary>
/// A render's progress as its job shows it: the stage and the step of the ladder for the job, the
/// share for the segment, which is checked once the file is written.
/// </summary>
/// <param name="share">The part of the segment's share the render takes: a publication leaves the rest to the send</param>
internal sealed class SegmentJobProgress(JobRun run, string segmentId, double share = 1) : IProgress<RenderProgress>
{
    public void Report(RenderProgress value)
    {
        run.Stage(StageOf(value.Stage), value.Attempt is { } attempt ? new JobAttempt(attempt.N, attempt.Concessions) : null);
        run.Item(segmentId, value.Stage == RenderStage.Validate ? JobItemState.Validating : JobItemState.Exporting, value.Fraction * share);
    }

    private static JobStage StageOf(RenderStage stage) => stage switch
    {
        RenderStage.Probe => JobStage.Probe,
        RenderStage.Pass1 => JobStage.Pass1,
        RenderStage.Pass2 => JobStage.Pass2,
        RenderStage.Validate => JobStage.Validate,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "A stage the contract does not know"),
    };
}
