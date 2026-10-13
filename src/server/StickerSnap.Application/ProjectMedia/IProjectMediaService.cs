using StickerSnap.Core.Errors;

namespace StickerSnap.Application.ProjectMedia;

/// <summary>
/// The media of a project the browser loads by itself (contract, group media): the video of the
/// editor's &lt;video&gt;, the cover of its card, the frames of its timeline.
/// </summary>
public interface IProjectMediaService
{
    /// <summary>
    /// GET /projects/{id}/media: the source itself when the browser plays it, else a playable copy
    /// with the same frames, made on the first request.
    /// </summary>
    /// <exception cref="AppException">
    /// project_not_found, project_invalid, project_too_new; source_not_found when the video is gone;
    /// render_failed, disk_full, ffmpeg_missing from making the copy
    /// </exception>
    Task<MediaFile> VideoAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// GET /projects/{id}/cover: the first sticker's middle frame. A cover made before is served
    /// when the video is gone, so the card keeps its picture.
    /// </summary>
    /// <exception cref="AppException">As <see cref="VideoAsync"/></exception>
    Task<MediaFile> CoverAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>GET /projects/{id}/frames: <paramref name="count"/> frames from <paramref name="from"/> in one picture.</summary>
    /// <exception cref="AppException">bad_request for frames, a count or a height out of range; as <see cref="VideoAsync"/></exception>
    Task<MediaFile> FramesAsync(Guid id, int from, int count, int height, CancellationToken cancellationToken);
}

/// <summary>A file to answer with.</summary>
/// <param name="Tag">The same as long as the content is: the ETag the browser revalidates with</param>
public sealed record MediaFile(string Path, string ContentType, string Tag);
