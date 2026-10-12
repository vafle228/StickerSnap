using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Render;

/// <summary>
/// Renders every segment of a project file with <see cref="SegmentRenderService"/>, each beside
/// the project as `&lt;project&gt;.&lt;segment&gt;.webm` (`.draft.webm` for a draft). For a person
/// checking the engine by hand, before the SPA can ask for an export.
/// </summary>
public sealed class ProjectRenderService(IProjectStore projects, SegmentRenderService engine)
{
    /// <param name="progress">Gets each segment's progress under its id</param>
    /// <exception cref="AppException">As <see cref="IProjectStore.ReadAsync"/>; a segment's own failure is in its outcome</exception>
    public async Task<IReadOnlyList<SegmentOutcome>> RenderAsync(
        string projectPath,
        RenderEffort quality,
        IProgress<SegmentProgress>? progress,
        CancellationToken cancellationToken)
    {
        var project = await projects.ReadAsync(projectPath, cancellationToken);
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var name = Path.GetFileNameWithoutExtension(projectPath);
        var suffix = quality == RenderEffort.Draft ? ".draft" : "";
        List<SegmentOutcome> outcomes = [];
        foreach (var segment in project.Scene.Segments)
        {
            var request = new SegmentRender(
                project.Source.Path,
                project.Source.Media,
                project.Scene,
                segment.Id,
                quality,
                Path.Combine(directory, $"{name}.{segment.Id}{suffix}.webm"));
            var segmentProgress = progress is null ? null : new Tagged(segment.Id, progress);
            try
            {
                outcomes.Add(new SegmentOutcome(segment.Id, await engine.RenderAsync(request, segmentProgress, cancellationToken), null));
            }
            catch (AppException e)
            {
                outcomes.Add(new SegmentOutcome(segment.Id, null, e));
            }
        }

        return outcomes;
    }

    private sealed class Tagged(string segmentId, IProgress<SegmentProgress> target) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => target.Report(new SegmentProgress(segmentId, value));
    }
}

/// <param name="Sticker">Null when the segment made no sticker</param>
/// <param name="Failure">Why it made none: its code, message and the engine's log</param>
public sealed record SegmentOutcome(string SegmentId, RenderedSegment? Sticker, AppException? Failure);

public sealed record SegmentProgress(string SegmentId, RenderProgress Progress);
