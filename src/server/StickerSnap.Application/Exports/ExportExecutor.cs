using StickerSnap.Application.Files;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Render;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Exports;

/// <summary>
/// «Экспорт WEBM» (3c E): the final files of saved segments, one after another, by the revision the
/// SPA saved last. Each goes to the stickers folder as «Проект — Отрезок.webm», replacing its own
/// last export, and its result into the project's exports without a new revision (G7). A segment
/// that fails is left failed and the others go on, so the job may end partial.
/// </summary>
public sealed class ExportExecutor(
    IProjectService projects,
    ISettingsService settings,
    ISegmentRenderService engine,
    IStickerFolder folder,
    IRenderedFiles files) : IJobExecutor
{
    public JobKind Kind => JobKind.Export;

    /// <inheritdoc />
    /// <exception cref="AppException">
    /// revision_conflict when the scene was saved since the export was asked for; what reading the
    /// project throws; source_not_found
    /// </exception>
    public async Task RunAsync(JobRun run, CancellationToken cancellationToken)
    {
        var request = (JobRequest.Export)run.Request;
        var project = (await RenderSources.ProjectWithSourceAsync(projects, request.ProjectId, cancellationToken)).Project;
        if (project.Revision != request.Revision)
            throw new AppException(
                ErrorCode.RevisionConflict,
                $"Project {project.Id} is at revision {project.Revision}, the export was asked for {request.Revision}");

        var stickers = (await settings.GetAsync(cancellationToken)).Folders.Stickers;
        foreach (var segmentId in run.SegmentsToDo)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var segment = project.Scene.Segments.FirstOrDefault(segment => segment.Id == segmentId)
                    ?? throw new AppException(ErrorCode.BadRequest, $"Project {project.Id} has no segment {segmentId}");
                run.Done(segmentId, export: await ExportAsync(run, project, segment, stickers, cancellationToken));
            }
            catch (AppException exception)
            {
                run.ItemFailed(segmentId, exception);
            }
        }
    }

    private async Task<SegmentExport> ExportAsync(
        JobRun run, Project project, Segment segment, string stickers, CancellationToken cancellationToken)
    {
        run.Item(segment.Id, JobItemState.Exporting, 0);
        var last = project.Exports.GetValueOrDefault(segment.Id);
        var slot = await folder.ReserveAsync(stickers, cancellationToken);
        string path;
        RenderedSegment rendered;
        try
        {
            rendered = await engine.RenderAsync(
                new SegmentRender(
                    project.Source.Path, project.Source.Media, project.Scene, segment.Id, RenderEffort.Final, slot.TemporaryPath),
                new SegmentJobProgress(run, segment.Id),
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
