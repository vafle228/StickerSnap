using StickerSnap.Application.Files;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Render;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Exports;

/// <inheritdoc />
public sealed class SegmentExporter(
    IProjectService projects,
    ISegmentRenderService engine,
    IStickerFolder folder,
    IRenderedFiles files) : ISegmentExporter
{
    /// <inheritdoc />
    public async Task<SegmentExport> ExportAsync(
        Project project,
        Segment segment,
        string stickersFolder,
        IProgress<RenderProgress> progress,
        CancellationToken cancellationToken)
    {
        var last = project.Exports.GetValueOrDefault(segment.Id);
        var slot = await folder.ReserveAsync(stickersFolder, cancellationToken);
        string path;
        RenderedSegment rendered;
        try
        {
            rendered = await engine.RenderAsync(
                new SegmentRender(
                    project.Source.Path, project.Source.Media, project.Scene, segment.Id, RenderEffort.Final, slot.TemporaryPath),
                progress,
                cancellationToken);
            path = await folder.KeepAsync(
                slot, StickerFileNames.Candidates(project.Name, segment.Name), last?.Path, cancellationToken);
        }
        catch
        {
            folder.Discard(slot);
            throw;
        }

        // The file has its name: from here on its record follows it, whatever happens to the job
        var fileId = await files.AddAsync(path, CancellationToken.None);
        if (last is not null) await files.RemoveAsync(last.FileId, CancellationToken.None);

        var export = new SegmentExport(
            segment.Id,
            project.Revision,
            SegmentSignature.Of(segment, project.Source),
            fileId,
            path,
            rendered.Bytes,
            rendered.Output,
            rendered.Crf,
            rendered.Concessions,
            rendered.Ladder,
            rendered.Validation ?? throw new InvalidOperationException("A final render comes checked"));
        await projects.RecordExportAsync(project.Id, export, CancellationToken.None);
        return export;
    }
}
