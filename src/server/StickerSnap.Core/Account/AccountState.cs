namespace StickerSnap.Core.Account;

/// <summary>
/// This computer's link to the user's messenger account and its transitions: unlinked → pending →
/// linked, a code that expires or is declined, a check that finds the bot blocked, the service out
/// of reach or the computer unlinked from the phone, unlinking. A link in progress does not forget
/// the account linked before it, so «Отмена» and a declined code give it back.
/// </summary>
/// <param name="User">Whom this computer publishes for; null when it is not linked</param>
/// <param name="Device">This computer's name as the user sees it in their devices; set with <paramref name="User"/></param>
/// <param name="Problem">What the last check found wrong with <paramref name="User"/>'s chat</param>
/// <param name="Pending">The link in progress, if any</param>
/// <param name="Ended">How the last link ended without linking; shown while nothing is linked</param>
public sealed record AccountState(
    LinkedUser? User,
    string? Device,
    AccountProblem? Problem,
    PendingLink? Pending,
    LinkEnd? Ended)
{
    public static readonly AccountState Unlinked = new(null, null, null, null, null);

    public static AccountState Linked(LinkedUser user, string device) => new(user, device, null, null, null);

    /// <summary>Whether a code was given out and has not expired at <paramref name="now"/>.</summary>
    public bool IsWaiting(DateTimeOffset now) => Pending is { } pending && now < pending.ExpiresAt;

    /// <summary>What the SPA sees at <paramref name="now"/>: a link in progress first, then the account.</summary>
    public AccountLink ViewAt(DateTimeOffset now) => this switch
    {
        _ when IsWaiting(now) => new AccountLink.Pending(Pending!.DeepLink, Pending.ExpiresAt),
        { User: { } user, Device: { } device, Problem: { } problem } => new AccountLink.Failed(problem, user, device),
        { User: { } user, Device: { } device } => new AccountLink.Linked(user, device),
        _ => new AccountLink.Unlinked(Pending is null ? Ended : LinkEnd.Expired),
    };

    /// <summary>Gives out a new code; one given out before stops linking.</summary>
    public AccountState Start(PendingLink link) => this with { Pending = link, Ended = null };

    /// <summary>
    /// The user confirmed <paramref name="code"/>: this computer is <paramref name="device"/> of
    /// <paramref name="user"/> now, or null when the code is not the one waiting.
    /// </summary>
    public AccountState? Accept(string code, LinkedUser user, string device) =>
        Pending is { } pending && string.Equals(pending.Code, code, StringComparison.Ordinal) ? Linked(user, device) : null;

    /// <summary>The code <paramref name="code"/> ended without linking; another one waiting is left alone.</summary>
    public AccountState End(string code, LinkEnd reason) =>
        Pending is { } pending && string.Equals(pending.Code, code, StringComparison.Ordinal)
            ? this with { Pending = null, Ended = reason }
            : this;

    /// <summary>«Отмена» while waiting: the account as it was before the link.</summary>
    public AccountState Cancel() => this with { Pending = null, Ended = null };

    /// <summary>Drops a code that has expired at <paramref name="now"/>.</summary>
    public AccountState Expire(DateTimeOffset now) =>
        Pending is { } pending && now >= pending.ExpiresAt ? End(pending.Code, LinkEnd.Expired) : this;

    /// <summary>«Отвязать»: no account and no link in progress.</summary>
    public AccountState Unlink() => Unlinked;

    /// <summary>
    /// What a check found: the account the service has for this computer, or none when it was
    /// unlinked from the phone. A link in progress goes on either way.
    /// </summary>
    public AccountState Checked(LinkedUser? user, string? device, AccountProblem? problem) =>
        user is not null && device is not null
            ? this with { User = user, Device = device, Problem = problem }
            : this with { User = null, Device = null, Problem = null };

    /// <summary>The service is out of reach: a linked account shows it, an unlinked one has nothing to show.</summary>
    public AccountState Unreachable() => User is null ? this : this with { Problem = AccountProblem.NetworkUnavailable };
}
