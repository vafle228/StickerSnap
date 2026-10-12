namespace StickerSnap.Core.Media;

/// <summary>The source formats the editor opens (contract, SourceInfo.format).</summary>
public enum MediaFormat
{
    Mp4,
    Mov,
    Webm,
    Gif,
    Webp,
}

public static class MediaFormats
{
    private static readonly HashSet<string> Extensions = ["mp4", "mov", "webm", "gif", "webp"];

    /// <summary>
    /// Whether a file with this extension is worth handing to the media engine. Only the first
    /// check: the format itself is told by the container and the codec, not by the name.
    /// </summary>
    /// <param name="extension">Lower case, without the dot: `mp4`</param>
    public static bool IsSupportedExtension(string extension) => Extensions.Contains(extension);
}
