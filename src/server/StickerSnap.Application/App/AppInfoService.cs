using StickerSnap.Application.Media;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Application.App;

/// <summary>GET /api/v1/app: versions, the mode, the bot and Telegram's limits.</summary>
public sealed class AppInfoService(IHostInfo host, IMediaEngine engine)
{
    /// <summary>The contract's version (info.version of src/contract/openapi.yaml).</summary>
    public const string ApiVersion = "1.0.0";

    public async Task<AppInfo> GetAsync(CancellationToken cancellationToken) => new(
        host.AppVersion,
        await engine.GetInfoAsync(cancellationToken),
        ApiVersion,
        host.Mode,
        TelegramLimits.BotUsername,
        TelegramLimits.Current);
}
