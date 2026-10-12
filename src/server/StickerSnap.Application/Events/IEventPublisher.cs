using StickerSnap.Application.Projects;
using StickerSnap.Core.Settings;

namespace StickerSnap.Application.Events;

/// <summary>
/// The events of the hub /hubs/events (contract, x-events): a whole snapshot of what changed after
/// the answer to a request. A lost event breaks nothing, so publishing never fails a use case.
/// Each feature adds its event here as it arrives (jobUpdated, projectChanged, …).
/// </summary>
public interface IEventPublisher
{
    /// <summary>settingsChanged: after PATCH /api/v1/settings and after a publication (recent emoji).</summary>
    Task SettingsChangedAsync(UserSettings settings, CancellationToken cancellationToken);

    /// <summary>projectChanged: after a save, a rename or an open, and when a card read from its file changed.</summary>
    Task ProjectChangedAsync(ProjectListItem project, CancellationToken cancellationToken);

    /// <summary>projectRemoved: after «Убрать из списка» or «Удалить с диска».</summary>
    Task ProjectRemovedAsync(Guid id, CancellationToken cancellationToken);
}
