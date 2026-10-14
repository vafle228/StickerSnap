using StickerSnap.Core.Account;

namespace StickerSnap.Application.Account;

/// <summary>/api/v1/account: linking the user's Telegram account to the product bot (artboard 5).</summary>
public interface IAccountService
{
    /// <summary>GET /api/v1/account: the link as it is, without asking the messenger.</summary>
    Task<AccountLink> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// POST /api/v1/account/link: a new one-time code and the link to the bot that sends it back
    /// with Start; the bot's messages are read until then. The end comes as accountChanged.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">bot_unavailable without a bot token, network_unavailable</exception>
    Task<AccountLink> StartLinkAsync(CancellationToken cancellationToken);

    /// <summary>DELETE /api/v1/account/link: «Отмена» while waiting for Start, otherwise «Отвязать».</summary>
    Task<AccountLink> CancelOrUnlinkAsync(CancellationToken cancellationToken);

    /// <summary>POST /api/v1/account/check, «Проверить связь»: whether the bot can still act for the user.</summary>
    /// <exception cref="Core.Errors.AppException">network_unavailable when no account is linked and the bot is out of reach</exception>
    Task<AccountLink> CheckAsync(CancellationToken cancellationToken);
}
