namespace StickerSnap.Application.App;

/// <summary>GET /api/v1/app: versions, the mode, the bot and Telegram's limits.</summary>
public interface IAppInfoService
{
    Task<AppInfo> GetAsync(CancellationToken cancellationToken);
}
