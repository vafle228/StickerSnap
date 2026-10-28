namespace StickerSnap.Infrastructure.Links;

/// <summary>Where to look for yt-dlp first: StickerSnap__YtDlpDir.</summary>
public sealed class YtDlpOptions
{
    public const string Section = "StickerSnap";

    /// <summary>
    /// The directory with yt-dlp. Not set by the installed app, which ships it next to the
    /// server; in development it points at tools/yt-dlp/&lt;rid&gt; (npm run fetch:ytdlp).
    /// </summary>
    public string? YtDlpDir { get; set; }
}
