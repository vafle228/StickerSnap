using StickerSnap.Application.Projects;
using StickerSnap.Core.Projects;

namespace StickerSnap.Tests.Application.Projects;

/// <summary>The project list in memory, for the use cases' tests.</summary>
internal sealed class InMemoryProjectRegistry : IProjectRegistry
{
    private readonly Dictionary<Guid, ProjectCard> _cards = [];

    public IReadOnlyCollection<ProjectCard> Cards => _cards.Values;

    public Task<IReadOnlyList<ProjectCard>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProjectCard>>([.. _cards.Values]);

    public Task<ProjectCard?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_cards.GetValueOrDefault(id));

    public Task SaveAsync(ProjectCard card, CancellationToken cancellationToken)
    {
        _cards[card.Id] = card;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        _cards.Remove(id);
        return Task.CompletedTask;
    }
}
