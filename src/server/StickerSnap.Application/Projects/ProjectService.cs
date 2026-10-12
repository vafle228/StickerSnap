using System.Collections.Concurrent;
using StickerSnap.Application.Events;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Projects;

/// <summary>
/// «Проекты» and the editor's autosave (contract, group projects): the list from the registry, a
/// project from its own .ssproj. Changes of one project go one at a time, so an autosave and a
/// rename never both start from the same file; every change tells the windows by an event.
/// </summary>
public sealed class ProjectService(
    IProjectRegistry registry,
    IProjectStore store,
    ITrash trash,
    ISourcePresence sources,
    IEventPublisher events,
    TimeProvider clock) : IProjectService
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var cards = await registry.ListAsync(cancellationToken);
        return cards
            .OrderByDescending(card => card.UpdatedAt)
            .ThenBy(card => card.Name, StringComparer.CurrentCulture)
            .Select(ItemOf)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<OpenedProject> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new AppException(ErrorCode.BadRequest, "The path of a project must be absolute")
            {
                Fields = new Dictionary<string, string[]>
                {
                    ["path"] = [string.IsNullOrWhiteSpace(path) ? "empty" : "not_absolute"],
                },
            };

        var full = Path.GetFullPath(path);
        var project = await store.ReadAsync(full, cancellationToken);

        return await UnderLockAsync(project.Id, async () =>
        {
            foreach (var other in await registry.ListAsync(cancellationToken))
            {
                if (other.Id == project.Id || !SamePath(other.Path, full)) continue;
                await registry.RemoveAsync(other.Id, cancellationToken);
                await events.ProjectRemovedAsync(other.Id, cancellationToken);
            }

            var item = await ChangedAsync(full, project, cancellationToken);
            return new OpenedProject(project, full, item.SourceStatus);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OpenedProject> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var (card, project) = await ReadAsync(id, cancellationToken);
        var item = await RefreshAsync(card, project, cancellationToken);
        return new OpenedProject(project, card.Path, item.SourceStatus);
    }

    /// <inheritdoc />
    public async Task<ProjectListItem> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        var wanted = name.Trim();
        if (wanted.Length == 0)
            throw new AppException(ErrorCode.BadRequest, "A project needs a name")
            {
                Fields = new Dictionary<string, string[]> { ["name"] = ["empty"] },
            };

        return await UnderLockAsync(id, async () =>
        {
            var (card, project) = await ReadAsync(id, cancellationToken);
            if (project.Name == wanted) return await RefreshAsync(card, project, cancellationToken);

            var renamed = project with { Name = wanted, SavedAt = clock.GetUtcNow() };
            await store.WriteAsync(card.Path, renamed, cancellationToken);
            return await ChangedAsync(card.Path, renamed, cancellationToken);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SavedScene> SaveSceneAsync(Guid id, int revision, Scene scene, CancellationToken cancellationToken) =>
        UnderLockAsync(id, async () =>
        {
            var (card, project) = await ReadAsync(id, cancellationToken);
            if (revision != project.Revision)
                throw new AppException(
                    ErrorCode.RevisionConflict,
                    $"Project {id} is at revision {project.Revision}, the save was made over {revision}");

            SceneValidator.EnsureValid(scene, project.Source.Media);

            var saved = project with { Scene = scene, Revision = project.Revision + 1, SavedAt = clock.GetUtcNow() };
            await store.WriteAsync(card.Path, saved, cancellationToken);
            await ChangedAsync(card.Path, saved, cancellationToken);
            return new SavedScene(saved.Revision, saved.SavedAt);
        }, cancellationToken);

    /// <inheritdoc />
    public Task RemoveAsync(Guid id, RemoveMode mode, CancellationToken cancellationToken) =>
        UnderLockAsync(id, async () =>
        {
            var card = await FindAsync(id, cancellationToken);
            if (mode == RemoveMode.Trash) await trash.MoveToTrashAsync(card.Path, cancellationToken);

            await registry.RemoveAsync(id, cancellationToken);
            await events.ProjectRemovedAsync(id, cancellationToken);
            return true;
        }, cancellationToken);

    private ProjectListItem ItemOf(ProjectCard card) => new(card, sources.Of(card.SourcePath));

    private async Task<ProjectCard> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await registry.FindAsync(id, cancellationToken)
        ?? throw new AppException(ErrorCode.ProjectNotFound, $"Project {id} is not on the list");

    /// <summary>The card and the project its file holds; a file that now holds another project is not this one.</summary>
    private async Task<(ProjectCard Card, Project Project)> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var card = await FindAsync(id, cancellationToken);
        var project = await store.ReadAsync(card.Path, cancellationToken);
        if (project.Id != id)
            throw new AppException(ErrorCode.ProjectNotFound, $"{card.Path} holds project {project.Id} now, not {id}");
        return (card, project);
    }

    /// <summary>Brings the card in line with a file changed outside the app, and says so.</summary>
    private async Task<ProjectListItem> RefreshAsync(ProjectCard card, Project project, CancellationToken cancellationToken) =>
        ProjectCard.Of(project, card.Path) == card
            ? ItemOf(card)
            : await ChangedAsync(card.Path, project, cancellationToken);

    private async Task<ProjectListItem> ChangedAsync(string path, Project project, CancellationToken cancellationToken)
    {
        var card = ProjectCard.Of(project, path);
        await registry.SaveAsync(card, cancellationToken);
        var item = ItemOf(card);
        await events.ProjectChangedAsync(item, cancellationToken);
        return item;
    }

    private async Task<T> UnderLockAsync<T>(Guid id, Func<Task<T>> change, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await change();
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool SamePath(string a, string b) => string.Equals(
        Path.GetFullPath(a),
        Path.GetFullPath(b),
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
