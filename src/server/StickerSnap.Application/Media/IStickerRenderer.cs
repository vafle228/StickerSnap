using StickerSnap.Core.Media;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Media;

/// <summary>Renders a sticker file from a source with the media engine.</summary>
public interface IStickerRenderer
{
    /// <summary>Writes the sticker of <paramref name="render"/> to its output path, replacing a file there.</summary>
    /// <exception cref="Core.Errors.AppException">
    /// `render_failed` with the engine's words in `log` when the engine fails; `ffmpeg_missing`
    /// when the engine is missing or too old
    /// </exception>
    Task<RenderedSticker> RenderAsync(StickerRender render, CancellationToken cancellationToken);
}

/// <param name="Source">What the probe read from <paramref name="SourcePath"/></param>
/// <param name="OutputPath">A .webm file</param>
public sealed record StickerRender(
    string SourcePath,
    MediaInfo Source,
    StickerSpec Spec,
    RateControl Rate,
    RenderEffort Effort,
    string OutputPath);

public sealed record RenderedSticker(string Path, long SizeBytes);
