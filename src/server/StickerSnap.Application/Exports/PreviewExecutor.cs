using StickerSnap.Application.Files;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Render;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Exports;

/// <summary>
/// «Точное превью» (3c D): a draft of the segment as the editor has it, saved or not, fitted the way
/// the export will fit it but from quick one-pass drafts, into a temporary file the SPA fetches by
/// GET /api/v1/files. Its size is what the final sticker is expected to weigh, since the draft
/// itself is larger. A failure fails the job with its code.
/// </summary>
public sealed class PreviewExecutor(IProjectService projects, ISegmentRenderService engine, IRenderedFiles files) : IJobExecutor
{
    public JobKind Kind => JobKind.Preview;

    /// <inheritdoc />
    public async Task RunAsync(JobRun run, CancellationToken cancellationToken)
    {
        var request = (JobRequest.Preview)run.Request;
        var segment = request.Segment;
        var project = (await RenderSources.ProjectWithSourceAsync(projects, request.ProjectId, cancellationToken)).Project;

        run.Item(segment.Id, JobItemState.Exporting, 0);
        var draft = files.NewDraft();
        var rendered = await engine.RenderAsync(
            new SegmentRender(
                project.Source.Path,
                project.Source.Media,
                project.Scene with { Segments = [segment] },
                segment.Id,
                RenderEffort.Draft,
                draft.Path),
            new SegmentJobProgress(run, segment.Id),
            cancellationToken);

        run.Done(segment.Id, preview: new PreviewFile(draft.Id, rendered.Ladder[^1].Bytes));
    }
}
