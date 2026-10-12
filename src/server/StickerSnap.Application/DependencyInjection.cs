using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.App;
using StickerSnap.Application.Projects;
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
        .AddSingleton<AppInfoService>()
        .AddSingleton<SettingsService>()
        .AddSingleton<ProjectService>()
        .AddSingleton<StickerFitService>()
        .AddSingleton<CorpusRenderService>()
        .AddSingleton<RenderPlanService>()
        .AddSingleton<SourceFiles>()
        .AddSingleton<ValidationService>();
}
