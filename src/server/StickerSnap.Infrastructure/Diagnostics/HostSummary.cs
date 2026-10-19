using System.Runtime.InteropServices;
using StickerSnap.Application.App;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Cloud;

namespace StickerSnap.Infrastructure.Diagnostics;

/// <summary>
/// The head of a report: the app's, the media engine's, the runtime's and the shell's versions, the mode and
/// the system; and what of this computer's must not leave it: the user's name and the device key.
/// </summary>
internal sealed class HostSummary(IHostInfo host, IMediaEngine engine, ThisComputer computer, DeviceCredentials credentials)
    : ISystemSummary
{
    public async Task<IReadOnlyList<string>> DescribeAsync(CancellationToken cancellationToken)
    {
        var media = await engine.GetInfoAsync(cancellationToken);
        return
        [
            $"StickerSnap {computer.AppVersion} ({Mode()})",
            $"{media.Name} {media.Version ?? "-"} ({Status(media.Status)})",
            RuntimeInformation.FrameworkDescription,
            .. host.ShellVersion is { } shell ? [shell] : Array.Empty<string>(),
            $"{computer.Platform} ({RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()})",
        ];
    }

    /// <summary>The login and the name of the home folder, which may differ from it.</summary>
    public IReadOnlyList<string> UserNames { get; } =
    [
        .. new[] { Environment.UserName, Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    public async Task<IReadOnlyList<string>> SecretsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await credentials.GetKeyAsync(cancellationToken) is { } key ? [key] : [];
        }
        catch (AppException)
        {
            // An unreadable device file holds no key a report could leak
            return [];
        }
    }

    private string Mode() => host.Mode == AppMode.Desktop ? "desktop" : "web";

    private static string Status(MediaEngineStatus status) => status switch
    {
        MediaEngineStatus.Ok => "ok",
        MediaEngineStatus.Missing => "missing",
        _ => "too old",
    };
}
