using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;

namespace StickerSnap.Infrastructure.Links;

/// <summary>
/// What a failed yt-dlp said, as the SPA knows it: its `ERROR:` line read for the reasons the
/// contract names. What is not recognised is site_changed with the log, worth a report: either
/// the site changed and yt-dlp needs an update, or something new went wrong.
/// </summary>
internal static class YtDlpErrors
{
    private static readonly string[] Gone =
    [
        "Video unavailable", "Private video", "This video has been removed", "This video is no longer available",
        "This video is not available", "This video is unavailable", "account associated with this video has been terminated",
        "HTTP Error 404", "HTTP Error 410", "members-only", "not available in your country",
    ];

    private static readonly string[] Adult = ["Sign in to confirm your age", "age-restricted", "inappropriate for some users"];

    private static readonly string[] Live = ["live event will begin", "Premieres in", "This live event", "is live"];

    private static readonly string[] Unreachable =
    [
        "getaddrinfo failed", "Name or service not known", "Temporary failure in name resolution", "nodename nor servname",
        "timed out", "Connection refused", "Network is unreachable", "No route to host", "Connection reset",
        "Connection aborted", "Remote end closed connection", "Failed to resolve", "Unable to connect",
        "No address associated", "Errno 11001", "Errno 10060", "Errno 10061", "Errno 10065",
    ];

    private static readonly string[] NoSpace = ["No space left on device", "Errno 28", "There is not enough space on the disk"];

    /// <param name="log">The tail of yt-dlp's stderr</param>
    /// <param name="command">The command, for the developer's report</param>
    /// <param name="url">The video's link, for the log</param>
    public static AppException Of(string log, LinkSite linkSite, string url, string command)
    {
        var site = linkSite.ToString().ToLowerInvariant();
        var error = log.Split('\n').LastOrDefault(line => line.StartsWith("ERROR:", StringComparison.Ordinal)) ?? log;
        var context = new Dictionary<string, string> { ["command"] = command };

        if (Has(error, NoSpace))
            return new AppException(ErrorCode.DiskFull, $"No space left to download {url}") { Log = log, Context = context };
        if (Has(error, Adult)) return Unavailable("adult", url, log, context);
        if (Has(error, Live)) return Unavailable("live", url, log, context);
        if (Has(error, Gone)) return Unavailable("gone", url, log, context);
        if (error.Contains("Unsupported URL", StringComparison.Ordinal))
            return new AppException(ErrorCode.LinkUnsupported, $"{url} is no video yt-dlp knows")
            {
                Params = new Dictionary<string, object> { ["reason"] = "site" },
                Log = log,
                Context = context,
            };
        if (Has(error, Unreachable))
            return new AppException(ErrorCode.SiteUnreachable, $"{site} did not answer for {url}")
            {
                Params = new Dictionary<string, object> { ["site"] = site },
                Log = log,
                Context = context,
            };
        return new AppException(ErrorCode.SiteChanged, $"yt-dlp failed on {url}: {error}")
        {
            Params = new Dictionary<string, object> { ["site"] = site },
            Log = log,
            Context = context,
        };
    }

    private static bool Has(string error, string[] phrases) =>
        phrases.Any(phrase => error.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static AppException Unavailable(string reason, string url, string log, Dictionary<string, string> context) =>
        new(ErrorCode.VideoUnavailable, $"{url} cannot be downloaded: {reason}")
        {
            Params = new Dictionary<string, object> { ["reason"] = reason },
            Log = log,
            Context = context,
        };
}
