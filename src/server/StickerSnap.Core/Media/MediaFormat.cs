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

    /// <summary>The extension a file of this format is saved with: `mp4`.</summary>
    public static string ExtensionOf(MediaFormat format) => format.ToString().ToLowerInvariant();

    /// <summary>How many bytes from the start of a file <see cref="Sniff"/> needs to tell its format.</summary>
    public const int SniffBytes = 64;

    /// <summary>
    /// The format told by the file's first bytes, whatever its name says: GIF and WEBP by their
    /// signatures, WEBM by the EBML header's DocType (a Matroska .mkv is not one), MP4 and MOV by the
    /// ftyp box, MOV by the brand `qt  `. Null for anything else, such as AVI.
    /// </summary>
    /// <param name="head">The first <see cref="SniffBytes"/> bytes, or the whole file when it is shorter</param>
    public static MediaFormat? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8)) return MediaFormat.Gif;
        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return MediaFormat.Webp;
        if (head.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
            return head.IndexOf("webm"u8) >= 0 ? MediaFormat.Webm : null;
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8))
            return head[8..12].SequenceEqual("qt  "u8) ? MediaFormat.Mov : MediaFormat.Mp4;
        return null;
    }
}
