namespace StickerSnap.Tests;

/// <summary>
/// Values of the "Category" trait. Untagged tests are fast and run in CI's job «Server» on every
/// push; a <see cref="Slow"/> one is left out there and runs in a workflow of its own.
/// </summary>
public static class Category
{
    /// <summary>Runs real FFmpeg over media files, or takes seconds for another reason.</summary>
    public const string Slow = "Slow";
}
