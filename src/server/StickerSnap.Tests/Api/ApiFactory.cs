using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Api.Shell;
using StickerSnap.Application.Ffmpeg;

namespace StickerSnap.Tests.Api;

/// <summary>
/// The whole server in memory, with its own wwwroot (no `npm run build` needed) and the settings
/// the desktop shell would pass as StickerSnap__*. The environment is Testing, so a SPA built into
/// StickerSnap.Api/wwwroot is not served over this one as Development's static web assets.
/// No real process is started: FFmpeg is <see cref="Ffmpeg"/>, missing until a test says otherwise.
/// </summary>
public class ApiFactory(params (string Key, string Value)[] shell) : WebApplicationFactory<Program>
{
    public const string Page = "<!doctype html><div id=\"root\"></div>";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("stickersnap-wwwroot-").FullName;

    public IFfmpegLocator Ffmpeg { get; } = MissingFfmpeg();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), Page);
        builder.UseEnvironment("Testing");
        builder.UseWebRoot(_webRoot);
        foreach (var (key, value) in shell) builder.UseSetting($"{ShellOptions.Section}:{key}", value);

        builder.ConfigureTestServices(services => services.AddSingleton(Ffmpeg));
    }

    private static IFfmpegLocator MissingFfmpeg()
    {
        var ffmpeg = Substitute.For<IFfmpegLocator>();
        ffmpeg.FindAsync(Arg.Any<CancellationToken>()).Returns(FfmpegLookup.Missing);
        return ffmpeg;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // The factory disposes twice when it is disposed by `using`
        if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
    }
}
