namespace StickerSnap.Infrastructure.Telegram;

// The Bot API's own objects (core.telegram.org/bots/api), only the fields StickerSnap reads.
// BotApiClient's JSON settings write their names in snake_case.

/// <summary>Every answer of the Bot API: ok with a result, or not ok with an error code and its description.</summary>
internal sealed record BotResponse<T>(bool Ok, T? Result, int? ErrorCode, string? Description, ResponseParameters? Parameters);

/// <param name="RetryAfter">Seconds to wait after a 429</param>
internal sealed record ResponseParameters(int? RetryAfter);

internal sealed record BotUser(long Id, bool IsBot, string FirstName, string? LastName, string? Username);

internal sealed record BotChatRef(long Id);

/// <param name="Date">Unix time</param>
internal sealed record BotMessage(long MessageId, BotUser? From, BotChatRef Chat, long Date, string? Text);

internal sealed record BotUpdate(long UpdateId, BotMessage? Message);

/// <param name="StickerType">regular, mask or custom_emoji</param>
internal sealed record BotStickerSet(string Name, string Title, string StickerType, BotSticker[] Stickers);

internal sealed record BotSticker(string FileId, string FileUniqueId, string? Emoji);

/// <param name="FilePath">Where /file/bot&lt;token&gt;/ serves it, for at least an hour</param>
internal sealed record BotFile(string FileId, string FileUniqueId, long? FileSize, string? FilePath);

/// <param name="Sticker">A file_id, or attach://&lt;part&gt; for a file sent in the same request</param>
internal sealed record BotInputSticker(string Sticker, string Format, IReadOnlyList<string> EmojiList);
