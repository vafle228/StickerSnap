using StickerSnap.Core.Account;

namespace StickerSnap.Application.Account;

/// <summary>
/// Links this computer to the user's messenger account: the computer asks for a code, the user
/// confirms it on their phone, and from then on this computer acts on the user's behalf with a
/// key of its own. The key stays behind this port: the use cases only learn whether the computer
/// is linked and to whom. Which service does the linking is decided by the registration in
/// StickerSnap.Infrastructure.
/// </summary>
/// <remarks>
/// A failure is an <see cref="Core.Errors.AppException"/>: network_unavailable, bot_unavailable
/// (the service is down or not configured), rate_limited (params.retryAfterSec).
/// </remarks>
public interface IDeviceLinking
{
    /// <summary>A new code for this computer, valid for a few minutes.</summary>
    Task<LinkInvite> InviteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What the user answered to <paramref name="invite"/>, waiting up to <paramref name="wait"/>
    /// (at most 25 s) while they have not. On <see cref="LinkAnswer.Linked"/> the computer keeps
    /// its key, so the next calls act for that user.
    /// </summary>
    Task<LinkAnswer> WaitAsync(LinkInvite invite, TimeSpan wait, CancellationToken cancellationToken);

    /// <summary>Burns the code: the user who opens it later is told it is out of date. A code already gone is no error.</summary>
    Task CancelAsync(LinkInvite invite, CancellationToken cancellationToken);

    /// <summary>
    /// The account this computer is linked to now, as the service sees it; null when it is not
    /// linked or the user has unlinked it from their phone, in which case the computer forgets
    /// its key.
    /// </summary>
    Task<LinkedDevice?> GetAsync(CancellationToken cancellationToken);

    /// <summary>Unlinks this computer and forgets its key. A computer that is not linked stays so.</summary>
    Task UnlinkAsync(CancellationToken cancellationToken);
}

/// <param name="Code">What the link carries; the computer shows the link and its QR, not the code</param>
/// <param name="Link">Opens the confirmation in the messenger, for the button and the QR</param>
/// <param name="Secret">Proves this computer asked for the code, to wait for it and to cancel it</param>
public sealed record LinkInvite(string Code, string Link, DateTimeOffset ExpiresAt, string Secret);

/// <summary>Where a code stands: <see cref="IDeviceLinking.WaitAsync"/>'s answer.</summary>
public abstract record LinkAnswer
{
    private LinkAnswer()
    {
    }

    /// <summary>The user has not answered yet.</summary>
    public sealed record Waiting(DateTimeOffset ExpiresAt) : LinkAnswer;

    /// <summary>The user pressed «Привязать»: this computer is theirs now.</summary>
    public sealed record Linked(LinkedDevice Device) : LinkAnswer;

    /// <summary>The user pressed «Это не я».</summary>
    public sealed record Declined : LinkAnswer;

    /// <summary>The code ran out, was cancelled or was answered before: a new one is needed.</summary>
    public sealed record Expired : LinkAnswer;
}

/// <param name="Name">The computer's name as the user sees it in their list of devices</param>
/// <param name="User">Whom the computer acts for</param>
/// <param name="BotBlocked">The user has stopped the bot: no messages in the chat, and publishing fails with bot_blocked</param>
public sealed record LinkedDevice(string Id, string Name, DateTimeOffset LinkedAt, LinkedUser User, bool BotBlocked);
