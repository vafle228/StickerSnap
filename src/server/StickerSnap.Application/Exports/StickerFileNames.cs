using StickerSnap.Application.Sources;

namespace StickerSnap.Application.Exports;

/// <summary>The names an exported sticker may take in the stickers folder.</summary>
public static class StickerFileNames
{
    public const string Extension = ".webm";

    /// <summary>
    /// `Кот — Стикер 1.webm`, then `Кот — Стикер 1 (2).webm`… for segment «Стикер 1» of project «Кот».
    /// </summary>
    public static IEnumerable<string> Candidates(string projectName, string segmentName)
    {
        // A slash in a name is a character, not a folder: it must not cut the name short
        var name = $"{projectName.Trim()} — {segmentName.Trim()}".Replace('/', '_').Replace('\\', '_');
        var stem = SafeFileNames.Of(name);
        yield return stem + Extension;
        for (var n = 2; ; n++) yield return $"{stem} ({n}){Extension}";
    }
}
