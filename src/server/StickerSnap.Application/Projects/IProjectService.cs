using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Projects;

/// <summary>«Проекты» and the editor's autosave: the list, one project and its changes.</summary>
public interface IProjectService
{
    /// <summary>GET /projects: newest first, each with whether its source is still there.</summary>
    Task<IReadOnlyList<ProjectListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// POST /projects (3c, «Читаю файл»): reads the video and makes a project of it with
    /// <see cref="DefaultScene"/>, in a new .ssproj in the projects folder named after the video.
    /// Cancelling while the video is read leaves nothing behind: that is «Отменить».
    /// </summary>
    /// <exception cref="AppException">
    /// bad_request for a path in web mode; source_not_found, format_unsupported, source_unreadable,
    /// ffmpeg_missing from reading it; save_failed, disk_full
    /// </exception>
    Task<OpenedProject> CreateAsync(SourceRef source, CancellationToken cancellationToken);

    /// <summary>
    /// PUT /projects/{id}/source, «Указать новое видео» for «Исходник не найден»: the project keeps
    /// its scene and its revision and takes the new video; segments past its end are named.
    /// </summary>
    /// <exception cref="AppException">project_not_found; what <see cref="CreateAsync"/> throws for the video</exception>
    Task<ReplacedSource> ReplaceSourceAsync(Guid id, SourceRef source, CancellationToken cancellationToken);

    /// <summary>
    /// POST /projects/open, also «Вернуть» after «Убрать из списка»: reads the file and puts it on
    /// the list. The id in the file decides which project it is: a project moved or copied to
    /// another path is listed at the path opened last, and a path that now holds another project
    /// loses the card of the old one.
    /// </summary>
    /// <exception cref="AppException">bad_request for a path that is not absolute; what reading the file throws</exception>
    Task<OpenedProject> OpenAsync(string path, CancellationToken cancellationToken);

    /// <summary>GET /projects/{id}: the project as its file has it now.</summary>
    /// <exception cref="AppException">
    /// project_not_found when the id is not on the list or its file is gone; project_invalid, project_too_new
    /// </exception>
    Task<OpenedProject> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// PATCH /projects/{id} (F2): the name inside the file; the file keeps its own name on disk.
    /// The revision stays, so the editor's next autosave is not refused.
    /// </summary>
    /// <exception cref="AppException">bad_request for an empty name; project_not_found; save_failed, disk_full</exception>
    Task<ProjectListItem> RenameAsync(Guid id, string name, CancellationToken cancellationToken);

    /// <summary>
    /// PUT /projects/{id}/scene, the editor's autosave: the whole scene over the revision the SPA
    /// last saw. The scene must keep its rules against the source; the revision grows by one.
    /// </summary>
    /// <exception cref="AppException">
    /// revision_conflict when the file was saved since <paramref name="revision"/>; bad_request for a
    /// scene that breaks a rule; project_not_found; save_failed, disk_full
    /// </exception>
    Task<SavedScene> SaveSceneAsync(Guid id, int revision, Scene scene, CancellationToken cancellationToken);

    /// <summary>
    /// An export's result into the project's file, as the last export of its segment (contract,
    /// Project.exports). The revision stays (G7), so the editor's next autosave is not refused.
    /// </summary>
    /// <exception cref="AppException">project_not_found; save_failed, disk_full</exception>
    Task RecordExportAsync(Guid id, SegmentExport export, CancellationToken cancellationToken);

    /// <summary>
    /// DELETE /projects/{id}: off the list, and with <see cref="RemoveMode.Trash"/> its .ssproj to
    /// the system's trash. The source video and the exported stickers are never touched.
    /// </summary>
    /// <exception cref="AppException">project_not_found when the id is not on the list; save_failed</exception>
    Task RemoveAsync(Guid id, RemoveMode mode, CancellationToken cancellationToken);
}
