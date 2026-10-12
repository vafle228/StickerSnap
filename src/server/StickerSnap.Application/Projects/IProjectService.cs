using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Projects;

/// <summary>«Проекты» and the editor's autosave: the list, one project and its changes.</summary>
public interface IProjectService
{
    /// <summary>GET /projects: newest first, each with whether its source is still there.</summary>
    Task<IReadOnlyList<ProjectListItem>> ListAsync(CancellationToken cancellationToken);

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
    /// DELETE /projects/{id}: off the list, and with <see cref="RemoveMode.Trash"/> its .ssproj to
    /// the system's trash. The source video and the exported stickers are never touched.
    /// </summary>
    /// <exception cref="AppException">project_not_found when the id is not on the list; save_failed</exception>
    Task RemoveAsync(Guid id, RemoveMode mode, CancellationToken cancellationToken);
}
