using StickerSnap.Core.Settings;

namespace StickerSnap.Application.Settings;

/// <summary>Where the settings are kept between launches.</summary>
public interface ISettingsRepository
{
    /// <summary>The saved settings, or the defaults when nothing is saved or the saved file is broken.</summary>
    Task<UserSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>Saves all of them at once: a failure leaves the previous settings whole.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken);
}
