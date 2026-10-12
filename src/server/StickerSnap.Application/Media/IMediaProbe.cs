using StickerSnap.Core.Media;

namespace StickerSnap.Application.Media;

/// <summary>Reads a source's parameters with the media engine.</summary>
public interface IMediaProbe
{
    /// <summary>What the file at <paramref name="path"/> holds.</summary>
    /// <exception cref="Core.Errors.AppException">
    /// `format_unsupported` with `params.ext` for an extension or a container the editor does not
    /// open; `source_not_found` when there is no such file; `source_unreadable` with the engine's
    /// words in `log` when it cannot read it; `ffmpeg_missing` when the engine is missing or too old
    /// </exception>
    Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken);
}
