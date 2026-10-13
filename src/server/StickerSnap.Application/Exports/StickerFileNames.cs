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
        var stem = SafeFileNames.Of($"{projectName.Trim()} — {segmentName.Trim()}");
        yield return stem + Extension;
        for (var n = 2; ; n++) yield return $"{stem} ({n}){Extension}";
    }
}
