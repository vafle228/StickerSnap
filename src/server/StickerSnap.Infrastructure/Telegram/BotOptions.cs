namespace StickerSnap.Infrastructure.Telegram;

/// <summary>
/// The product bot's settings: StickerSnap__BotToken and StickerSnap__BotApiUrl. The token is never
/// set from the interface: in development it is a user secret
/// (`dotnet user-secrets set StickerSnap:BotToken &lt;token&gt; --project src/server/StickerSnap.Api`),
/// in the installed app it is <see cref="BotToken.FileName"/> in the data directory.
/// </summary>
public sealed class BotOptions
{
    public const string Section = "StickerSnap";

    /// <summary>The token of @StickerSnapAppBot from @BotFather; wins over the secrets file when set.</summary>
    public string? BotToken { get; set; }

    /// <summary>Where the Bot API answers; another address only for a local Bot API server.</summary>
    public Uri BotApiUrl { get; set; } = new("https://api.telegram.org");
}
