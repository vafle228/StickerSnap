namespace StickerSnap.Core.Media;

/// <summary>
/// Whether the editor's &lt;video&gt; can play a source as it is (contract, getProjectMedia). The
/// timeline counts frame n at n/fps, so a source plays as it is only when the browser plays it and
/// its frames sit at those times; any other one gets a playable copy with the same frames.
/// </summary>
public static class BrowserPlayback
{
    /// <summary>
    /// An MP4 with H.264, or a WEBM with VP8, VP9 or AV1, at a steady rate. GIF and WEBP are
    /// pictures, not video, to a &lt;video&gt;; MOV and other codecs are not played by every browser.
    /// </summary>
    public static bool PlaysAsIs(MediaInfo source) =>
        !source.IsVariableFrameRate && source.Fps.IsValid && source switch
        {
            { Format: MediaFormat.Mp4, Codec: "h264" } => true,
            { Format: MediaFormat.Webm, Codec: "vp8" or "vp9" or "av1" } => true,
            _ => false,
        };

    /// <summary>The Content-Type a source that <see cref="PlaysAsIs"/> goes with.</summary>
    public static string ContentTypeOf(MediaFormat format) => format switch
    {
        MediaFormat.Mp4 => "video/mp4",
        MediaFormat.Webm => "video/webm",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "a format the browser is not given as it is"),
    };
}
