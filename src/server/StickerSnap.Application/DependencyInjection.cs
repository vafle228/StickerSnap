using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.Account;
using StickerSnap.Application.App;
using StickerSnap.Application.Estimates;
using StickerSnap.Application.Exports;
using StickerSnap.Application.Feedback;
using StickerSnap.Application.Files;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Packs;
using StickerSnap.Application.ProjectMedia;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Publish;
using StickerSnap.Application.Render;
using StickerSnap.Application.Settings;
using StickerSnap.Application.Sources;
using StickerSnap.Application.Validation;

namespace StickerSnap.Application;

public static class DependencyInjection
{
    /// <summary>
    /// The use cases. Each feature registers its services here; the ports they call
    /// (repositories, the media engine, the Bot API) are implemented in StickerSnap.Infrastructure.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services) => services
        .AddSingleton<IAppInfoService, AppInfoService>()
        .AddSingleton<ISettingsService, SettingsService>()
        .AddSingleton<IAccountService, AccountService>()
        .AddSingleton<IFeedbackService, FeedbackService>()
        .AddSingleton<IPackService, PackService>()
        .AddSingleton<IProjectService, ProjectService>()
        .AddSingleton<IProjectMediaService, ProjectMediaService>()
        .AddSingleton<IEstimateService, EstimateService>()
        .AddSingleton<IJobQueue, JobQueue>()
        .AddSingleton<IJobService, JobService>()
        .AddSingleton<IJobExecutor, PreviewExecutor>()
        .AddSingleton<IJobExecutor, ExportExecutor>()
        .AddSingleton<IJobExecutor, PublishExecutor>()
        .AddSingleton<ISegmentExporter, SegmentExporter>()
        .AddSingleton<IPublishDraftService, PublishDraftService>()
        .AddSingleton<IFileService, FileService>()
        .AddSingleton<IStickerFitService, StickerFitService>()
        .AddSingleton<ISegmentRenderService, SegmentRenderService>()
        .AddSingleton<ICorpusRenderService, CorpusRenderService>()
        .AddSingleton<IRenderPlanService, RenderPlanService>()
        .AddSingleton<IProjectRenderService, ProjectRenderService>()
        .AddSingleton<SourceFiles>()
        .AddSingleton<SourceImport>()
        .AddSingleton<IUploadService, UploadService>()
        .AddSingleton<IValidationService, ValidationService>();
}
