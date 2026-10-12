using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Projects;

/// <summary>Where a project lives: its own file, at a path the user chose or the projects folder.</summary>
public interface IProjectStore
{
    /// <summary>The project saved at <paramref name="path"/>, in the current form whatever version wrote it.</summary>
    /// <exception cref="Core.Errors.AppException">
    /// project_not_found when nothing can be read there; project_invalid when it is not a project,
    /// with the broken property in the log; project_too_new when a newer app wrote it
    /// </exception>
    Task<Project> ReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Saves the whole project: a failure leaves the previous file whole.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task WriteAsync(string path, Project project, CancellationToken cancellationToken);
}
