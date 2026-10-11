using System.Runtime.InteropServices;
using StickerSnap.Api.Endpoints;
using StickerSnap.Api.Events;
using StickerSnap.Api.Json;
using StickerSnap.Api.Problems;
using StickerSnap.Api.Shell;
using StickerSnap.Application;
using StickerSnap.Application.App;
using StickerSnap.Application.Events;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure;
using StickerSnap.Infrastructure.Ffmpeg;

var builder = WebApplication.CreateBuilder(args);

// The composition root: the only place that knows Infrastructure. Endpoints call the Application.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOpenApi();

// The contract's JSON and its errors: Problem Details with a code and a trace id.
builder.Services.ConfigureHttpJsonOptions(options => ContractJson.Apply(options.SerializerOptions));
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = Problem.Complete);
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
// A body that does not bind (bad JSON, an unknown enum value or field) throws, so it gets the Problem too
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// The hub only sends events, with the same JSON as REST.
builder.Services.AddSignalR().AddJsonProtocol(options => ContractJson.Apply(options.PayloadSerializerOptions));
builder.Services.AddSingleton<IEventPublisher, HubEventPublisher>();

// The desktop shell passes its token, the mode and its pid (src/desktop/src/server.ts).
builder.Services.Configure<ShellOptions>(builder.Configuration.GetSection(ShellOptions.Section));
builder.Services.AddSingleton<IHostInfo, ShellHostInfo>();
builder.Services.AddHostedService<ParentWatch>();

// In development FFmpeg comes from tools/ffmpeg/<rid>, where npm run fetch:ffmpeg puts it.
if (builder.Environment.IsDevelopment())
    builder.Services.PostConfigure<FfmpegOptions>(options => options.FfmpegDir ??= Path.Combine(
        builder.Environment.ContentRootPath, "..", "..", "..", "tools", "ffmpeg", RuntimeInformation.RuntimeIdentifier));

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<TokenGate>();

// The server's own OpenAPI document, /openapi/v1.json, to compare with src/contract/openapi.yaml.
if (app.Environment.IsDevelopment()) app.MapOpenApi();

// The SPA is built by Vite into wwwroot (npm run build) and served from here.
// During development Vite serves it on 127.0.0.1:5173 and proxies /api and /hubs to this server.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGroup("/api/v1").MapApp().MapSettings();
app.MapHub<EventsHub>(EventsHub.Path);

// An API path without a handler is a mistake of the SPA: bad_request, never the SPA page.
// An unknown hub is a plain 404, as SignalR answers.
app.MapFallback("/api/{**path}", (HttpRequest request) =>
    TypedResults.Problem(Problem.Of(ErrorCode.BadRequest, $"No endpoint for {request.Method} {request.Path}")));
app.MapFallback("/hubs/{**path}", () => Results.NotFound());

// No router in the SPA: every other path gets index.html.
app.MapFallbackToFile("index.html");

app.Run();

// WebApplicationFactory<Program> in StickerSnap.Tests starts the app from here.
public partial class Program;
