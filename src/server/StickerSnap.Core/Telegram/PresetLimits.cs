namespace StickerSnap.Core.Telegram;

/// <summary>What Telegram accepts for one kind of video sticker.</summary>
/// <param name="MaxBytes">Size of the .webm file</param>
/// <param name="Side">The side that must be exact, in px; the other one is at most this</param>
public sealed record PresetLimits(int MaxBytes, int Side, double MaxDurationSec, int MaxFps);
