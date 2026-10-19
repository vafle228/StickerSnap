using System.Globalization;
using System.Runtime.InteropServices;
using StickerSnap.Application.App;
using StickerSnap.Application.Feedback;
using StickerSnap.Application.Media;
using StickerSnap.Infrastructure.Cloud;

namespace StickerSnap.Infrastructure.Diagnostics;

/// <summary>
/// The report of a bug as far as the server knows it without a log: the app's, the media engine's
/// and the runtime's versions, the mode, the system, and the error the window was opened from.
/// F4 adds the server's log and the error's context by its trace id.
/// </summary>
internal sealed class HostDiagnostics(IHostInfo host, IMediaEngine engine, ThisComputer computer, TimeProvider clock)
    : IDiagnosticsCollector
{
    public async Task<string> CollectAsync(FeedbackError? error, CancellationToken cancellationToken)
    {
        var media = await engine.GetInfoAsync(cancellationToken);
        var lines = new List<string>
        {
            $"StickerSnap {computer.AppVersion} ({Mode()})",
            $"{media.Name} {media.Version ?? "-"} ({Status(media.Status)})",
            RuntimeInformation.FrameworkDescription,
            $"{computer.Platform} ({RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()})",
            string.Create(CultureInfo.InvariantCulture, $"Sent at {clock.GetUtcNow():yyyy-MM-ddTHH:mm:ssZ}"),
        };
        if (error is not null) lines.Add($"Error: {error.Code}{(error.TraceId is { } trace ? $", trace {trace}" : "")}");

        // One line ending on every system: the report is read in the messenger, not on this computer
        return string.Join('\n', lines) + '\n';
    }

    private string Mode() => host.Mode == AppMode.Desktop ? "desktop" : "web";

    private static string Status(MediaEngineStatus status) => status switch
    {
        MediaEngineStatus.Ok => "ok",
        MediaEngineStatus.Missing => "missing",
        _ => "too old",
    };
}
