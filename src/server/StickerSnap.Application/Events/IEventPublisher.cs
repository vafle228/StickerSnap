using StickerSnap.Application.Projects;
using StickerSnap.Core.Account;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Links;
using StickerSnap.Core.Packs;
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

    /// <summary>jobUpdated: a job's state or progress changed, no more than 10 times a second; its end always.</summary>
    Task JobUpdatedAsync(Job job, CancellationToken cancellationToken);

    /// <summary>accountChanged: a link given out, Start pressed, a code expired, «Отмена», «Отвязать», a check that changed the state.</summary>
    Task AccountChangedAsync(AccountLink link, CancellationToken cancellationToken);

    /// <summary>packChanged: a check with Telegram, «Добавить по ссылке», a deleted sticker, and later a sent one.</summary>
    Task PackChangedAsync(Pack pack, CancellationToken cancellationToken);

    /// <summary>downloadUpdated: a download by link moved on, no more than 10 times a second; its end always.</summary>
    Task DownloadUpdatedAsync(Download download, CancellationToken cancellationToken);
}
