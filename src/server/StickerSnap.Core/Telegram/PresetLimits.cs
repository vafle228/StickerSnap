namespace StickerSnap.Core.Telegram;

/// <summary>What Telegram accepts for one kind of video sticker.</summary>
/// <param name="MaxBytes">Size of the .webm file</param>
/// <param name="Side">The side that must be exact, in px; the other one is at most this</param>
/// <param name="MaxDurationSec">Telegram's own limit: what the file's header may declare</param>
/// <param name="LongMaxDurationSec">
/// How long a long sticker (R19) may play: its header declares <see cref="LongSticker.DeclaredSec"/>
/// and its frames run on (spike B6)
/// </param>
public sealed record PresetLimits(int MaxBytes, int Side, double MaxDurationSec, double LongMaxDurationSec, int MaxFps)
{
    /// <summary>How long a segment may play: <see cref="LongMaxDurationSec"/> when it is a long sticker.</summary>
    public double MaxPlaySec(bool longSticker) => longSticker ? LongMaxDurationSec : MaxDurationSec;
}
