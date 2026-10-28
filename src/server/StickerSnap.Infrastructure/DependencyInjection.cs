using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Account;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Exports;
using StickerSnap.Application.Feedback;
using StickerSnap.Application.Files;
using StickerSnap.Application.Links;
using StickerSnap.Application.Media;
using StickerSnap.Application.Packs;
using StickerSnap.Application.ProjectMedia;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Settings;
using StickerSnap.Application.Sources;
using StickerSnap.Infrastructure.Account;
using StickerSnap.Infrastructure.Cloud;
using StickerSnap.Infrastructure.Diagnostics;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Infrastructure.Files;
using StickerSnap.Infrastructure.Links;
using StickerSnap.Infrastructure.Packs;
using StickerSnap.Infrastructure.Projects;
using StickerSnap.Infrastructure.Settings;
using StickerSnap.Infrastructure.Sources;
using StickerSnap.Infrastructure.Storage;
using StickerSnap.Infrastructure.Webm;

namespace StickerSnap.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The adapters behind the Application's ports: JSON-file repositories in the data directory,
    /// the media engine (FFmpeg and ffprobe processes), the cloud service's client. Each binds its
    /// options from <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DataOptions>(configuration.GetSection(DataOptions.Section));
        services.AddSingleton<IFolderAccess, FolderAccess>();
        services.AddSingleton<ISettingsRepository, JsonSettingsRepository>();
        services.AddSingleton<IAccountRepository, JsonAccountRepository>();
        services.AddSingleton<IPackRepository, JsonPackRepository>();
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

        // Videos by link: YouTube through yt-dlp, Giphy by its own addresses. Another downloader is registered here instead
        services.Configure<YtDlpOptions>(configuration.GetSection(YtDlpOptions.Section));
        services.AddSingleton<IYtDlpLocator, YtDlpLocator>();
        services.AddSingleton<IVideoSite, YtDlpVideoSite>();
        services.AddHttpClient(GiphySite.ClientName, client =>
        {
            client.Timeout = GiphySite.Timeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("StickerSnap");
        });
        services.AddSingleton<IVideoSite, GiphySite>();
        services.AddSingleton<IVideoSites, VideoSites>();

        // The cloud service holds the bot's token; this computer holds only its own key. Linking,
        // publishing and feedback all go through it: the desktop never calls the Bot API
        services.Configure<CloudOptions>(configuration.GetSection(CloudOptions.Section));
        services.AddSingleton<DeviceCredentials>();
        services.AddSingleton<ThisComputer>();
        services.AddHttpClient<CloudClient>(client => client.Timeout = CloudClient.Timeout);
        services.AddTransient<IDeviceLinking, CloudDeviceLinking>();
        services.AddTransient<IStickerPublisher, CloudStickerPublisher>();
        services.AddTransient<IFeedbackInbox, CloudFeedbackInbox>();
        services.AddSingleton<ISystemSummary, HostSummary>();

        // The server's log in the data directory, which a bug's report takes its lines from
        // A host without a clock of its own, such as a test's, logs by the system's
        services.AddSingleton(provider => new FileLog(
            provider.GetRequiredService<IOptions<DataOptions>>(), provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<FileLog>());
        services.AddSingleton<ILogArchive>(provider => provider.GetRequiredService<FileLog>());

        return services;
    }
}
