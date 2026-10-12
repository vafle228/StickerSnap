using StickerSnap.Core.Errors;
using StickerSnap.Core.Settings;

namespace StickerSnap.Application.Settings;

/// <summary>GET and PATCH /api/v1/settings: «Настройки» change at once, without «Сохранить».</summary>
public interface ISettingsService
{
    Task<UserSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies <paramref name="change"/>, saves the result and tells every window by settingsChanged.
    /// A new folder must be an absolute path the server can create and write in.
    /// </summary>
    /// <exception cref="AppException">bad_request for a wrong field, save_failed or disk_full</exception>
    Task<UserSettings> UpdateAsync(SettingsChange change, CancellationToken cancellationToken);
}
