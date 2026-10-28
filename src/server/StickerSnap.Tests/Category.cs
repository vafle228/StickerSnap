namespace StickerSnap.Tests;

/// <summary>
/// Values of the "Category" trait. Untagged tests are fast and run in CI's job «Server» on every
/// push; a <see cref="Slow"/> one is left out there and runs in a workflow of its own, an
/// <see cref="Online"/> one only by hand.
/// </summary>
public static class Category
{
    /// <summary>Runs real FFmpeg over media files, or takes seconds for another reason.</summary>
    public const string Slow = "Slow";

    /// <summary>
    /// Goes to real sites on the internet (YouTube, Giphy). Also <see cref="Slow"/>, and left out on
    /// CI as well, where a site may refuse a data centre or be down: run by hand after moving to a
    /// newer yt-dlp, with `-trait "Category=Online"`.
    /// </summary>
    public const string Online = "Online";
}
