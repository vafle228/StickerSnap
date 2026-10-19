using System.Text.RegularExpressions;

namespace StickerSnap.Application.Diagnostics;

/// <summary>
/// Takes out of a report what is the user's: a path keeps only its file name, the system's user
/// name becomes &lt;user&gt;, keys and tokens become &lt;secret&gt;. Videos and projects never get in.
/// </summary>
public sealed partial class ReportScrubber(IReadOnlyList<string> userNames, IReadOnlyList<string> secrets)
{
    public const string User = "<user>";
    public const string Secret = "<secret>";
    public const string Folder = "<folder>";

    /// <summary>A key this short would be found inside ordinary words.</summary>
    private const int ShortestSecret = 8;

    public string Scrub(string text)
    {
        foreach (var secret in secrets.Where(secret => secret.Length >= ShortestSecret))
            text = text.Replace(secret, Secret, StringComparison.Ordinal);
        text = BotToken().Replace(text, Secret);
        text = Credential().Replace(text, match => match.Groups["name"].Value + match.Groups["sep"].Value + Secret);
        text = WindowsPath().Replace(text, FileName);
        text = UnixPath().Replace(text, FileName);
        foreach (var name in userNames.Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            text = Regex.Replace(
                text, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_])", User, RegexOptions.IgnoreCase);
        }
        return text;
    }

    /// <summary>A path is its last part: «C:\Users\Ivan\Videos\cat.mp4» is «cat.mp4».</summary>
    private static string FileName(Match path) =>
        path.Groups["name"].Value is { Length: > 0 } name ? name : Folder;

    /// <summary>A Telegram bot's token, as in api.telegram.org/bot123456:AA…/getMe.</summary>
    [GeneratedRegex(@"\d{6,12}:[A-Za-z0-9_-]{30,}")]
    private static partial Regex BotToken();

    /// <summary>A key or a token after its name: `Authorization: Device k3y`, `"deviceKey":"…"`, `token=…`.</summary>
    [GeneratedRegex(
        """(?<name>\b(?:authorization|devicekey|device_key|polltoken|poll_token|token|access_token|api_?key|secret|password|initdata)\b)(?<sep>["']?\s*[:=]\s*["']?)(?:(?:Bearer|Device|tma|Basic)\s+)?[^\s"',;&]+""",
        RegexOptions.IgnoreCase)]
    private static partial Regex Credential();

    /// <summary>
    /// `C:\…\name`, `C:/…/name` or `\\server\…\name`, with single or JSON-doubled backslashes.
    /// Folders may hold spaces, since a slash follows them; the file name ends at a space.
    /// </summary>
    [GeneratedRegex(
        """(?<![\w])(?:[A-Za-z]:|\\\\[^\\/\s]+)(?:[\\/]+[^\\/:*?"<>|\r\n]+(?=[\\/]))*[\\/]+(?<name>[^\\/:*?"<>|\r\n\s]*)""")]
    private static partial Regex WindowsPath();

    /// <summary>An absolute path under a folder that holds users' or the system's files, or under ~.</summary>
    [GeneratedRegex(
        """(?<![\w.~/])(?:~|/(?:home|Users|tmp|var|private|mnt|media|opt|root|run|Volumes|srv|usr|etc))(?:/[^/:*?"<>|\r\n]+(?=/))*/(?<name>[^/:*?"<>|\r\n\s]*)""")]
    private static partial Regex UnixPath();
}
