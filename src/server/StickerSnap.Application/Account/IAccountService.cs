using StickerSnap.Core.Account;

namespace StickerSnap.Application.Account;

/// <summary>/api/v1/account: linking this computer to the user's Telegram account (artboards 5 and 18).</summary>
public interface IAccountService
{
    /// <summary>GET /api/v1/account: the link as it is, without asking the service.</summary>
    Task<AccountLink> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// POST /api/v1/account/link: a new code and the link that confirms it in the Mini App; the
    /// answer is waited for until then. The end comes as accountChanged.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">bot_unavailable without the service, network_unavailable, rate_limited</exception>
    Task<AccountLink> StartLinkAsync(CancellationToken cancellationToken);

    /// <summary>DELETE /api/v1/account/link: «Отмена» while waiting for the answer, otherwise «Отвязать».</summary>
    /// <exception cref="Core.Errors.AppException">network_unavailable or bot_unavailable when unlinking: the link stays</exception>
    Task<AccountLink> CancelOrUnlinkAsync(CancellationToken cancellationToken);

    /// <summary>
    /// POST /api/v1/account/check, «Проверить связь»: whether this computer is still linked and
    /// the bot can write to the user, as the service sees it.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">network_unavailable or bot_unavailable when no account is linked and the service is out of reach</exception>
    Task<AccountLink> CheckAsync(CancellationToken cancellationToken);
}
