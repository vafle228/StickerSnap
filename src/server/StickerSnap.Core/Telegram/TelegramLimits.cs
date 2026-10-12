using StickerSnap.Core.Projects;

namespace StickerSnap.Core.Telegram;

/// <summary>The limits of video stickers and packs in Telegram, as GET /api/v1/app reports them.</summary>
public sealed record TelegramLimits(
    PresetLimits Sticker,
    PresetLimits Emoji,
    int RegularPackCapacity,
    int EmojiPackCapacity)
{
    /// <summary>
    /// 256 KB, 3 s and 30 fps for both; 512 px for a sticker, 100 px for a custom emoji.
    /// The emoji gets the sticker's 256 KB too (contract, decided 11 October, gap G3).
    /// </summary>
    public static readonly TelegramLimits Current = new(
        Sticker: new PresetLimits(MaxBytes: 262_144, Side: 512, MaxDurationSec: 3, MaxFps: 30),
        Emoji: new PresetLimits(MaxBytes: 262_144, Side: 100, MaxDurationSec: 3, MaxFps: 30),
        RegularPackCapacity: 120,
        EmojiPackCapacity: 200);

    /// <summary>The limits of a sticker or a custom emoji.</summary>
    public PresetLimits For(SegmentPreset preset) => preset == SegmentPreset.Emoji ? Emoji : Sticker;

    /// <summary>The bot that creates the packs: their links end in _by_StickerSnapAppBot.</summary>
    public const string BotUsername = "StickerSnapAppBot";
}
