namespace StickerSnap.Application.Sources;

/// <summary>
/// A name that is safe as one file's name on every system the server runs on, made from whatever a
/// browser or a video's name gives: no folders, none of Windows' reserved characters, no trailing
/// dots or spaces.
/// </summary>
public static class SafeFileNames
{
    /// <summary>What a name with nothing usable left becomes.</summary>
    public const string Fallback = "video";

    private static readonly char[] Reserved = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <param name="name">A file's name, possibly with a path of either system before it</param>
    public static string Of(string? name)
    {
        var last = (name ?? "").Split('/', '\\')[^1];
        var safe = new string([.. last.Select(c => char.IsControl(c) || Reserved.Contains(c) ? '_' : c)])
            .Trim()
            .TrimEnd('.', ' ');
        return safe.Length == 0 || safe.All(c => c == '.') ? Fallback : safe;
    }
}
