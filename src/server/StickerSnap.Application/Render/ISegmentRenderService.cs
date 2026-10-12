using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Render;

/// <summary>The render engine: a segment of a scene becomes its sticker file.</summary>
public interface ISegmentRenderService
{
    /// <param name="progress">Gets <see cref="RenderProgress"/> as the contract's Job.progress has it, rising</param>
    /// <exception cref="AppException">
    /// `bad_request` when the scene has no such segment or the segment breaks a rule of the scene
    /// against the source; `cannot_fit` when the ladder ran out; `render_failed`, `disk_full` and
    /// `ffmpeg_missing` from the media engine; `source_unreadable` when the file it wrote cannot be
    /// read back. A cancelled render throws <see cref="OperationCanceledException"/>; neither a
    /// failed nor a cancelled render leaves a file being written or a temp file of the engine
    /// </exception>
    Task<RenderedSegment> RenderAsync(SegmentRender request, IProgress<RenderProgress>? progress, CancellationToken cancellationToken);
}
