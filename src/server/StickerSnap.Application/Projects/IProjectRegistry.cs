using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Projects;

/// <summary>
/// The list of projects the app knows (projects.json): which .ssproj each id lives in, and its card,
/// so «Проекты» is drawn without opening every file. The file itself stays the truth: a card is
/// refreshed whenever its project is read or saved.
/// </summary>
public interface IProjectRegistry
{
    /// <summary>Every card, in no particular order.</summary>
    Task<IReadOnlyList<ProjectCard>> ListAsync(CancellationToken cancellationToken);

    Task<ProjectCard?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds the card, or replaces the one with its id.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task SaveAsync(ProjectCard card, CancellationToken cancellationToken);

    /// <summary>Forgets the project; nothing happens for an id it does not know.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken);
}
