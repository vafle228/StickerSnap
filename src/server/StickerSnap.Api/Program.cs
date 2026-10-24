using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Events;
using StickerSnap.Api.Json;
using StickerSnap.Api.Observability;
using StickerSnap.Api.Problems;
using StickerSnap.Api.Shell;
using StickerSnap.Application;
using StickerSnap.Application.App;
using StickerSnap.Application.Events;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Api;

/// <summary>
/// The composition root: the only place that knows Infrastructure. Controllers call the Application.
/// WebApplicationFactory&lt;Program&gt; in StickerSnap.Tests starts the app from here.
/// </summary>
public sealed class Program
{
    private Program()
    {
    }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder.Services, builder.Configuration, builder.Environment);

        var app = builder.Build();
        Configure(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);

        // The contract's JSON, the same for controllers, problems and the hub.
        services.AddControllers()
            .AddJsonOptions(options => ContractJson.Apply(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = BadRequestOf);
        services.ConfigureHttpJsonOptions(options => ContractJson.Apply(options.SerializerOptions));
        services.AddOpenApi(options => options
            .AddDocumentTransformer<AbsentNullTransformer>()
            .AddOperationTransformer<MultipartFileTransformer>()
            .AddSchemaTransformer<ConstTransformer>()
            .AddSchemaTransformer<StringOrNumberValuesTransformer>()
            .AddSchemaTransformer<DiscriminatorTransformer>());

        // Errors are Problem Details with a code and a trace id.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = Problem.Complete);
        services.AddExceptionHandler<ProblemExceptionHandler>();

        // The hub only sends events.
        services.AddSignalR().AddJsonProtocol(options => ContractJson.Apply(options.PayloadSerializerOptions));
        services.AddSingleton<IEventPublisher, HubEventPublisher>();

        // The desktop shell passes its token, the mode and its pid (src/desktop/src/server.ts).
        services.Configure<ShellOptions>(configuration.GetSection(ShellOptions.Section));
        services.AddSingleton<IHostInfo, ShellHostInfo>();
        services.AddHostedService<ParentWatch>();

        // Traces, metrics and logs by OpenTelemetry: the web server only, never the desktop app.
        services.AddObservability(configuration);

        // In development FFmpeg comes from tools/ffmpeg/<rid>, where npm run fetch:ffmpeg puts it.
        if (environment.IsDevelopment())
        {
            var tools = Path.Combine(
                environment.ContentRootPath, "..", "..", "..", "tools", "ffmpeg", RuntimeInformation.RuntimeIdentifier);
            services.PostConfigure<FfmpegOptions>(options => options.FfmpegDir ??= tools);
        }
    }

    private static void Configure(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<TokenGate>();

        // The server's own OpenAPI document, /openapi/v1.json, to compare with src/contract/openapi.yaml.
        if (app.Environment.IsDevelopment()) app.MapOpenApi();

        // The SPA is built by Vite into wwwroot (npm run build) and served from here.
        // During development Vite serves it on 127.0.0.1:5173 and proxies /api and /hubs to this server.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapControllers();
        app.MapHub<EventsHub>(EventsHub.Path);

        // No router in the SPA: every other path gets index.html.
        app.MapFallbackToFile("index.html");
    }

    /// <summary>A body that does not bind (bad JSON, an unknown enum value or field) is bad_request.</summary>
    private static IActionResult BadRequestOf(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => $"{entry.Key}: {string.Join(" ", entry.Value!.Errors.Select(error => error.ErrorMessage))}");
        return new ProblemResult(Problem.Of(ErrorCode.BadRequest, string.Join("; ", errors)));
    }
}
