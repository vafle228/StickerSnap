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
}
