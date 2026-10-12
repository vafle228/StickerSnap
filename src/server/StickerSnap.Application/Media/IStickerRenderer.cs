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

    /// <summary>
    /// How many bytes the final render of <paramref name="probe"/> would take at its constant
    /// quality, found in a fraction of that render's time; no file is kept.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">As <see cref="RenderAsync"/></exception>
    Task<long> EstimateAsync(StickerProbe probe, CancellationToken cancellationToken);

    /// <summary>
    /// The commands the engine would run for <paramref name="render"/>, one per line, for a person
    /// to read and run by hand; nothing is run.
    /// </summary>
    string Describe(StickerRender render);
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

/// <param name="Crf">The constant quality of the final render it stands for</param>
public sealed record StickerProbe(string SourcePath, MediaInfo Source, StickerSpec Spec, int Crf);

public sealed record RenderedSticker(string Path, long SizeBytes);
