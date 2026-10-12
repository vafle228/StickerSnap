using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Render;

/// <summary>Renders every segment of a project file, each beside the project.</summary>
public interface IProjectRenderService
{
    /// <param name="progress">Gets each segment's progress under its id</param>
    /// <exception cref="AppException">As <see cref="IProjectStore.ReadAsync"/>; a segment's own failure is in its outcome</exception>
    Task<IReadOnlyList<SegmentOutcome>> RenderAsync(
        string projectPath,
        RenderEffort quality,
        IProgress<SegmentProgress>? progress,
        CancellationToken cancellationToken);
}
