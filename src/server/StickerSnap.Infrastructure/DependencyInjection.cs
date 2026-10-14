using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.Account;
using StickerSnap.Application.Exports;
using StickerSnap.Application.Files;
using StickerSnap.Application.Media;
using StickerSnap.Application.Packs;
using StickerSnap.Application.ProjectMedia;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Settings;
using StickerSnap.Application.Sources;
using StickerSnap.Infrastructure.Account;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Infrastructure.Files;
using StickerSnap.Infrastructure.Projects;
using StickerSnap.Infrastructure.Settings;
using StickerSnap.Infrastructure.Sources;
using StickerSnap.Infrastructure.Storage;
using StickerSnap.Infrastructure.Telegram;
using StickerSnap.Infrastructure.Webm;

namespace StickerSnap.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The adapters behind the Application's ports: JSON-file repositories in the data directory,
    /// the media engine (FFmpeg and ffprobe processes), the Telegram Bot API client. Each binds its
    /// options from <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DataOptions>(configuration.GetSection(DataOptions.Section));
        services.AddSingleton<IFolderAccess, FolderAccess>();
        services.AddSingleton<ISettingsRepository, JsonSettingsRepository>();
        services.AddSingleton<IAccountRepository, JsonAccountRepository>();
        services.AddSingleton<IProjectStore, ProjectFileStore>();
        services.AddSingleton<IProjectRegistry, JsonProjectRegistry>();
        services.AddSingleton<ISourcePresence, SourcePresence>();
        services.AddSingleton<ISourceWatch, FileSystemSourceWatch>();
        services.Configure<UploadOptions>(configuration.GetSection(UploadOptions.Section));
        services.AddSingleton<IUploadStore, UploadStore>();
        services.AddSingleton<IFileFingerprint, SampledFileFingerprint>();
        services.AddSingleton<IRenderedFiles, RenderedFileStore>();
        services.AddSingleton<IStickerFolder, StickerFolder>();
        services.AddSingleton<IMediaCache, MediaCache>();
        // The desktop builds are Windows and Linux; every other Unix keeps the same trash as Linux
        services.AddSingleton<ITrash>(provider => OperatingSystem.IsWindows()
            ? new WindowsRecycleBin()
            : FreedesktopTrash.ForUser(provider.GetRequiredService<TimeProvider>()));

        services.Configure<FfmpegOptions>(configuration.GetSection(FfmpegOptions.Section));
        services.AddSingleton<IFfmpegVersionReader, CliFfmpegVersionReader>();
        services.AddSingleton<IFfmpegLocator, FfmpegLocator>();
        services.AddSingleton<IFfmpeg, CliFfmpeg>();
        // The media engine of the Application's ports is FFmpeg: another engine is registered here instead
        services.AddSingleton<IMediaEngine, FfmpegEngine>();
        services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
        services.AddSingleton<IStickerRenderer, FfmpegStickerRenderer>();
        services.AddSingleton<IEditorMedia, FfmpegEditorMedia>();
        services.AddSingleton<IStickerHeader, WebmStickerHeader>();

        services.Configure<BotOptions>(configuration.GetSection(BotOptions.Section));
        services.AddSingleton<BotToken>();
        services.AddHttpClient<BotApiClient>(client => client.Timeout = BotApiClient.Timeout)
            // Every address of the Bot API holds the token, and the request log writes addresses out
            .RemoveAllLoggers();
        // The messenger of the Application's ports is Telegram, with the one product bot
        services.AddTransient<IStickerPublisher, TelegramStickerPublisher>();
        services.AddTransient<IBotChat, TelegramBotChat>();

        return services;
    }
}
