using StickerSnap.Application.Jobs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Projects;

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
    ISegmentExporter exporter) : IJobExecutor
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
                run.Item(segment.Id, JobItemState.Exporting, 0);
                var export = await exporter.ExportAsync(
                    project, segment, stickers, new SegmentJobProgress(run, segment.Id), cancellationToken);
                run.Done(segmentId, export: export);
            }
            catch (AppException exception)
            {
                run.ItemFailed(segmentId, exception);
            }
        }
    }
}
