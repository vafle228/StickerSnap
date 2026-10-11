using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.Ffmpeg;
using StickerSnap.Application.Settings;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Infrastructure.Settings;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The adapters behind the Application's ports: JSON-file repositories in the data directory,
    /// FFmpeg and ffprobe processes, the Telegram Bot API client. Each binds its options from
    /// <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DataOptions>(configuration.GetSection(DataOptions.Section));
        services.AddSingleton<IFolderAccess, FolderAccess>();
        services.AddSingleton<ISettingsRepository, JsonSettingsRepository>();

        services.Configure<FfmpegOptions>(configuration.GetSection(FfmpegOptions.Section));
        services.AddSingleton<IFfmpegVersionReader, CliFfmpegVersionReader>();
        services.AddSingleton<IFfmpegLocator, FfmpegLocator>();
        services.AddSingleton<IFfmpeg, CliFfmpeg>();
        services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();

        return services;
    }
}
