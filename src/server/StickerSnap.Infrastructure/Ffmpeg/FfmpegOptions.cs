namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>Where to look for FFmpeg first: StickerSnap__FfmpegDir.</summary>
public sealed class FfmpegOptions
{
    public const string Section = "StickerSnap";

    /// <summary>
    /// The directory with ffmpeg and ffprobe. Not set by the installed app, which ships them next
    /// to the server; in development it points at tools/ffmpeg/&lt;rid&gt; (npm run fetch:ffmpeg).
    /// </summary>
    public string? FfmpegDir { get; set; }
}
