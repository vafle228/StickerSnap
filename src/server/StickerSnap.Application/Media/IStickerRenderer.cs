using StickerSnap.Core.Media;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Media;

/// <summary>Renders a sticker file from a source with the media engine.</summary>
public interface IStickerRenderer
{
    /// <summary>
    /// Writes the sticker of <paramref name="render"/> to its output path, replacing a file there.
    /// A failed or cancelled render leaves no file there and none of its own temp files.
    /// </summary>
    /// <param name="progress">Gets how far each pass has got, as the engine reports it</param>
    /// <exception cref="Core.Errors.AppException">
    /// `render_failed` with the engine's words in `log` when the engine fails; `disk_full` when
    /// there is no room for the file or the engine's temp files; `ffmpeg_missing` when the engine
    /// is missing or too old
    /// </exception>
    Task<RenderedSticker> RenderAsync(StickerRender render, IProgress<PassProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// How many bytes the final render of <paramref name="probe"/> would take at its constant
    /// quality, found in a fraction of that render's time; no file is kept.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">As <see cref="RenderAsync"/></exception>
    Task<long> EstimateAsync(StickerProbe probe, CancellationToken cancellationToken);

    /// <summary>
    /// The quick draft <see cref="EstimateAsync"/> measures, kept at <paramref name="outputPath"/>
    /// for the eye: the preview of the final render, close to it but larger.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">As <see cref="RenderAsync"/></exception>
    Task<DraftSticker> DraftAsync(StickerProbe probe, string outputPath, CancellationToken cancellationToken);

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

/// <param name="SizeBytes">Of the draft on disk</param>
/// <param name="EstimatedBytes">Of the final render it stands for, as <see cref="IStickerRenderer.EstimateAsync"/> tells</param>
public sealed record DraftSticker(string Path, long SizeBytes, long EstimatedBytes);

/// <summary>How far a render has got: pass <paramref name="Pass"/> of <paramref name="Passes"/>, its share done.</summary>
/// <param name="Pass">From 1; a two-pass render first analyses the clip, then writes the file</param>
/// <param name="Fraction">0..1 of this pass</param>
public sealed record PassProgress(int Pass, int Passes, double Fraction);
