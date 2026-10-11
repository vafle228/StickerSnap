using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StickerSnap.Api.Shell;

namespace StickerSnap.Tests.Api;

/// <summary>
/// The whole server in memory, with its own wwwroot (no `npm run build` needed) and the settings
/// the desktop shell would pass as StickerSnap__*. The environment is Testing, so a SPA built into
/// StickerSnap.Api/wwwroot is not served over this one as Development's static web assets.
/// </summary>
public class ApiFactory(params (string Key, string Value)[] shell) : WebApplicationFactory<Program>
{
    public const string Page = "<!doctype html><div id=\"root\"></div>";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("stickersnap-wwwroot-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), Page);
        builder.UseEnvironment("Testing");
        builder.UseWebRoot(_webRoot);
        foreach (var (key, value) in shell) builder.UseSetting($"{ShellOptions.Section}:{key}", value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // The factory disposes twice when it is disposed by `using`
        if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
    }
}
