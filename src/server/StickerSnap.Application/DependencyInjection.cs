using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.App;
using StickerSnap.Application.Settings;

namespace StickerSnap.Application;

public static class DependencyInjection
{
    /// <summary>
    /// The use cases. Each feature registers its services here; the ports they call
    /// (repositories, the media engine, the Bot API) are implemented in StickerSnap.Infrastructure.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services) => services
        .AddSingleton<AppInfoService>()
        .AddSingleton<SettingsService>();
}
