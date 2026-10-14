namespace StickerSnap.Core.Telegram;

/// <summary>
/// The long sticker of R19 (spike B6, 12 October): Telegram takes a video sticker's length from
/// the header of its WEBM, while players play every frame. A sticker that plays longer than
/// Telegram's limit declares a length within it, its frames and their rate stay as rendered;
/// @fStikBot's stickers declare the same 2.9 s.
/// </summary>
public static class LongSticker
{
    /// <summary>The length a long sticker declares: under 3 s with room for a container's rounding.</summary>
    public const double DeclaredSec = 2.9;

    /// <summary>
    /// The length a sticker that plays <paramref name="playSec"/> declares in its header, or null
    /// when it plays within Telegram's limit and declares what it plays.
    /// </summary>
    public static double? DeclaredFor(double playSec, PresetLimits limits) =>
        playSec > limits.MaxDurationSec + 1e-9 ? DeclaredSec : null;
}
