using System.Globalization;
using System.Text.Json;
using StickerSnap.Application.Links;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;

namespace StickerSnap.Infrastructure.Links;

/// <summary>What yt-dlp prints: the JSON of `-J` with the format it picked, and the lines of its progress.</summary>
internal static class YtDlpOutput
{
    /// <summary>What each progress line starts with (<see cref="ProgressTemplate"/>), so other lines are told apart.</summary>
    public const string ProgressMark = "[stickersnap]";

    /// <summary>`--progress-template`: bytes so far, the size when known, its estimate, seconds left; NA for an unknown one.</summary>
    public const string ProgressTemplate =
        "download:" + ProgressMark +
        " %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s %(progress.eta)s";

    private static readonly HashSet<string> LiveStates = ["is_live", "is_upcoming", "post_live"];

    /// <summary>The video of `yt-dlp -J -f …`: its title and length, and the one format it picked.</summary>
    /// <exception cref="AppException">
    /// video_unavailable (live) for a stream; site_changed for JSON without what a video has
    /// </exception>
    public static LinkVideo Parse(string json, VideoLink link)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw Changed(link, $"yt-dlp printed no JSON: {exception.Message}", exception);
        }
        if (root.ValueKind != JsonValueKind.Object) throw Changed(link, "yt-dlp printed JSON that is no video");

        if (String(root, "live_status") is { } live && LiveStates.Contains(live))
            throw new AppException(ErrorCode.VideoUnavailable, $"{link.Url} is a live stream ({live})")
            {
                Params = new Dictionary<string, object> { ["reason"] = "live" },
            };

        var formatId = String(root, "format_id") ?? throw Changed(link, "yt-dlp picked no format");
        var extension = String(root, "ext") ?? throw Changed(link, "yt-dlp named no extension of the file");
        var duration = Number(root, "duration") ?? 0;
        var bytes = Number(root, "filesize") ?? Number(root, "filesize_approx")
            ?? (Number(root, "tbr") is { } kbps ? kbps * 1000 / 8 * duration : 0);

        return new LinkVideo(
            link.Site,
            link.Url,
            String(root, "title") ?? link.VideoId,
            duration,
            String(root, "thumbnail") is { } thumbnail && thumbnail.StartsWith("https://", StringComparison.Ordinal) ? thumbnail : null,
            (long)Math.Round(bytes),
            QualityOf(Number(root, "width"), Number(root, "height")) ?? extension.ToUpperInvariant(),
            formatId,
            extension);
    }

    /// <summary>«720p» by the shorter side, so a vertical Short of 720×1280 is 720p as well.</summary>
    internal static string? QualityOf(double? width, double? height) => (width, height) switch
    {
        ({ } w, { } h) => $"{(int)Math.Min(w, h)}p",
        (null, { } h) => $"{(int)h}p",
        _ => null,
    };

    /// <summary>A line of <see cref="ProgressTemplate"/>; null for any other line.</summary>
    public static DownloadProgress? Progress(string line)
    {
        var at = line.IndexOf(ProgressMark, StringComparison.Ordinal);
        if (at < 0) return null;
        var parts = line[(at + ProgressMark.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || Value(parts[0]) is not { } received) return null;
        return new DownloadProgress((long)received, Value(parts[3]));
    }

    private static double? Value(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static AppException Changed(VideoLink link, string message, Exception? inner = null) =>
        new(ErrorCode.SiteChanged, $"{link.Url}: {message}", inner)
        {
            Params = new Dictionary<string, object> { ["site"] = link.Site.ToString().ToLowerInvariant() },
            Log = message,
        };
}
