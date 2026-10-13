using StickerSnap.Application.Render;
using StickerSnap.Core.Render;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Application.Exports;

/// <summary>What the render engine gives back for a request, as if it had rendered it.</summary>
internal static class Renders
{
    /// <summary>
    /// The second step of the ladder taken; a final render is checked green, a draft is not
    /// checked, and the last step's bytes are what the final file weighs or would weigh.
    /// </summary>
    public static RenderedSegment Of(SegmentRender request, long bytes = 190_464) => new(
        request.SegmentId,
        request.Quality,
        request.OutputPath,
        request.Quality == RenderEffort.Final ? bytes : bytes * 3 / 2,
        new StickerOutput(512, 512, 24, 3),
        Crf: 40,
        SampleExports.Second.Concessions,
        [new LadderTry(SampleExports.First, 300_000), new LadderTry(SampleExports.Second, bytes)],
        request.Quality == RenderEffort.Final ? SampleExports.Green : null);

    /// <summary>The stages a final render reports, in order.</summary>
    public static void ReportStages(IProgress<RenderProgress>? progress)
    {
        progress?.Report(new RenderProgress(0, RenderStage.Probe, null));
        var second = new RenderAttempt(2, SampleExports.Second.Concessions);
        progress?.Report(new RenderProgress(0.3, RenderStage.Pass1, second));
        progress?.Report(new RenderProgress(0.7, RenderStage.Pass2, second));
        progress?.Report(new RenderProgress(0.95, RenderStage.Validate, null));
    }
}
