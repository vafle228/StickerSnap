using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Api;
using StickerSnap.Api.Shell;
using StickerSnap.Application.Media;

namespace StickerSnap.Tests.Api;

/// <summary>
/// The whole server in memory, with its own wwwroot (no `npm run build` needed) and the settings
/// the desktop shell would pass as StickerSnap__*. The environment is Testing, so a SPA built into
/// StickerSnap.Api/wwwroot is not served over this one as Development's static web assets.
/// No real process is started: the media engine is <see cref="Engine"/>, missing until a test
/// says otherwise. The data directory is a temporary one, never the user's.
/// </summary>
public class ApiFactory(params (string Key, string Value)[] shell) : WebApplicationFactory<Program>
{
    public const string Page = "<!doctype html><div id=\"root\"></div>";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("stickersnap-wwwroot-").FullName;

    private string? _dataDir;
    private bool _ownsDataDir;

    public IMediaEngine Engine { get; } = MissingEngine();

    /// <summary>StickerSnap__DataDir: a new temporary directory unless a test passes one to outlive the factory.</summary>
    public string DataDir
    {
        get
        {
            if (_dataDir is null)
            {
                _dataDir = Directory.CreateTempSubdirectory("stickersnap-data-").FullName;
                _ownsDataDir = true;
            }
            return _dataDir;
        }
        init => _dataDir = value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), Page);
        builder.UseEnvironment("Testing");
        builder.UseWebRoot(_webRoot);
        builder.UseSetting("StickerSnap:DataDir", DataDir);
        foreach (var (key, value) in shell) builder.UseSetting($"{ShellOptions.Section}:{key}", value);

        builder.ConfigureTestServices(services => services.AddSingleton(Engine));
    }

    private static IMediaEngine MissingEngine()
    {
        var engine = Substitute.For<IMediaEngine>();
        engine.GetInfoAsync(Arg.Any<CancellationToken>()).Returns(new MediaEngineInfo("FFmpeg", MediaEngineStatus.Missing, null));
        return engine;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // The factory disposes twice when it is disposed by `using`
        if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
        if (_ownsDataDir && Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
