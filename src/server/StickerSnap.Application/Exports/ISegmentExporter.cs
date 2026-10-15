using StickerSnap.Application.Render;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Exports;

/// <summary>
/// The final file of one saved segment, as «Экспорт WEBM» makes it and a publication before it
/// sends a segment without a fresh export: rendered to the stickers folder, recorded in the
/// project's exports.
/// </summary>
public interface ISegmentExporter
{
    /// <summary>
    /// Renders <paramref name="segment"/> of <paramref name="project"/> (opened with its video in
    /// place) into <paramref name="stickersFolder"/> as «Проект — Отрезок.webm», replacing its own
    /// last export, and writes the result into the project's exports without a new revision (G7).
    /// </summary>
    /// <exception cref="Core.Errors.AppException">What the render throws: cannot_fit, render_failed, disk_full, …</exception>
    Task<SegmentExport> ExportAsync(
        Project project,
        Segment segment,
        string stickersFolder,
        IProgress<RenderProgress> progress,
        CancellationToken cancellationToken);
}
