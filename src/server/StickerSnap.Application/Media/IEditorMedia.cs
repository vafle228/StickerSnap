using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Media;

/// <summary>
/// What the editor shows of a source, made by the media engine: its frames for the timeline, a
/// cover for the card, and a copy the browser plays. Frame n is the n-th frame the source decodes
/// to, as the scene and the render count them. Each method writes <c>outputPath</c>, replacing a
/// file there, and leaves no file there when it fails or is cancelled.
/// </summary>
/// <remarks>
/// Every method throws <see cref="Core.Errors.AppException"/>: `render_failed` with the engine's
/// words in `log` when the engine fails; `disk_full` when there is no room for the file;
/// `ffmpeg_missing` when the engine is missing or too old.
/// </remarks>
public interface IEditorMedia
{
    /// <summary>The frames of <paramref name="strip"/> in one row, as a WEBP picture.</summary>
    Task WriteStripAsync(string sourcePath, MediaInfo source, FrameStrip strip, string outputPath, CancellationToken cancellationToken);

    /// <summary>The frame of <paramref name="cover"/>, cropped and scaled, as a WEBP picture.</summary>
    Task WriteCoverAsync(string sourcePath, MediaInfo source, CoverShot cover, string outputPath, CancellationToken cancellationToken);

    /// <summary>
    /// A WEBM a browser plays: every frame of the source, frame n at n/fps for the source's average
    /// rate, transparent where the source is, without sound.
    /// </summary>
    Task WritePlayableAsync(string sourcePath, MediaInfo source, string outputPath, CancellationToken cancellationToken);
}
