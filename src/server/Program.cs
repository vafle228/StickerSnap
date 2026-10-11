using StickerSnap.Server.Shell;

var builder = WebApplication.CreateBuilder(args);

// The desktop shell passes its token, the mode and its pid (src/desktop/src/server.ts).
builder.Services.Configure<ShellOptions>(builder.Configuration.GetSection(ShellOptions.Section));
builder.Services.AddHostedService<ParentWatch>();

var app = builder.Build();

app.UseMiddleware<TokenGate>();

// The SPA is built by Vite into wwwroot (npm run build) and served from here.
// During development Vite serves it on 127.0.0.1:5173 and proxies /api and /hubs to this server.
app.UseDefaultFiles();
app.UseStaticFiles();

// Unknown API or hub paths are 404, never the SPA page.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/hubs/{**path}", () => Results.NotFound());

// No router in the SPA: every other path gets index.html.
app.MapFallbackToFile("index.html");

app.Run();

// WebApplicationFactory<Program> in tests/StickerSnap.Server.Tests starts the app from here.
public partial class Program;
