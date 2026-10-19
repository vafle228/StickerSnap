using System.Reflection;
using Microsoft.Extensions.Options;
using StickerSnap.Application.App;

namespace StickerSnap.Api.Shell;

/// <summary>The app's version from the build (Directory.Build.props), the mode and the version the shell passed.</summary>
public sealed class ShellHostInfo(IOptions<ShellOptions> options) : IHostInfo
{
    public string AppVersion { get; } = VersionOf(typeof(ShellHostInfo).Assembly);

    public AppMode Mode =>
        string.Equals(options.Value.Mode, "desktop", StringComparison.OrdinalIgnoreCase) ? AppMode.Desktop : AppMode.Web;

    public string? ShellVersion => options.Value.ShellVersion?.Trim() is { Length: > 0 } version ? version : null;

    /// <summary>"0.1.0", without the commit the SDK appends after a plus.</summary>
    internal static string VersionOf(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        return version.Split('+', 2)[0];
    }
}
