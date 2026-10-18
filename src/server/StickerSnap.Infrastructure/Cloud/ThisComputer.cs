using System.Runtime.InteropServices;
using StickerSnap.Application.App;

namespace StickerSnap.Infrastructure.Cloud;

/// <summary>
/// How this computer introduces itself to the cloud service: the lines «Компьютер», «Система» and
/// «Приложение» the user sees before linking it, and in their list of devices after.
/// </summary>
internal sealed class ThisComputer(IHostInfo host)
{
    private const int NameLimit = 64;
    private const int PlatformLimit = 64;
    private const int VersionLimit = 32;

    /// <summary>The computer's name, DESKTOP-7F3K2Q.</summary>
    public string Name { get; } = Cut(Environment.MachineName is { Length: > 0 } name ? name : "StickerSnap", NameLimit);

    public DeviceOs Os { get; } =
        OperatingSystem.IsWindows() ? DeviceOs.Windows : OperatingSystem.IsMacOS() ? DeviceOs.Macos : DeviceOs.Linux;

    /// <summary>The system as people call it: Windows 11, Ubuntu 24.04.1 LTS.</summary>
    public string Platform { get; } = Cut(PlatformOf(Environment.OSVersion.Version, ReleaseName()), PlatformLimit);

    public string AppVersion => Cut(host.AppVersion, VersionLimit);

    /// <param name="version">The kernel's version; on Windows its build tells 11 from 10</param>
    /// <param name="release">PRETTY_NAME of /etc/os-release on Linux</param>
    internal static string PlatformOf(Version version, string? release)
    {
        if (OperatingSystem.IsWindows()) return version is { Major: 10, Build: >= 22000 } ? "Windows 11" : $"Windows {version.Major}";
        if (OperatingSystem.IsMacOS()) return $"macOS {version.ToString(2)}";
        return release is { Length: > 0 } ? release : RuntimeInformation.OSDescription;
    }

    private static string? ReleaseName()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            return File.ReadLines("/etc/os-release")
                .Where(line => line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                .Select(line => line["PRETTY_NAME=".Length..].Trim('"'))
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Cut(string text, int limit) => text.Length <= limit ? text : text[..limit];
}
