namespace StickerSnap.Core.Account;

/// <summary>
/// The user's link to the product bot and its transitions: unlinked → pending → linked, a code
/// that expires or does not match, a check that finds the bot blocked or the messenger out of
/// reach, unlinking. A link in progress does not forget the account linked before it, so «Отмена»
/// gives it back.
/// </summary>
/// <param name="User">Who pressed Start last; null when no account is linked</param>
/// <param name="Problem">What the last check found wrong with <paramref name="User"/>'s chat</param>
/// <param name="Pending">The link in progress, if any</param>
public sealed record AccountState(LinkedUser? User, AccountProblem? Problem, PendingLink? Pending)
{
    public static readonly AccountState Unlinked = new(null, null, null);

    public static AccountState Linked(LinkedUser user) => new(user, null, null);

    /// <summary>Whether a code was given out and has not expired at <paramref name="now"/>.</summary>
    public bool IsWaiting(DateTimeOffset now) => Pending is { } pending && now < pending.ExpiresAt;

    /// <summary>What the SPA sees at <paramref name="now"/>: a link in progress first, then the account.</summary>
    public AccountLink ViewAt(DateTimeOffset now) => this switch
    {
        _ when IsWaiting(now) => new AccountLink.Pending(Pending!.DeepLink, Pending.ExpiresAt),
        { User: { } user, Problem: { } problem } => new AccountLink.Failed(problem, user),
        { User: { } user } => new AccountLink.Linked(user),
        _ => new AccountLink.Unlinked(),
    };

    /// <summary>Gives out a new code; one given out before stops linking.</summary>
    public AccountState Start(PendingLink link) => this with { Pending = link };

    /// <summary>
    /// Start pressed with <paramref name="code"/> by <paramref name="user"/>: the account linked,
    /// or null when the code is not the one waiting or has expired.
    /// </summary>
    public AccountState? Accept(string code, LinkedUser user, DateTimeOffset now) =>
        IsWaiting(now) && string.Equals(Pending!.Code, code, StringComparison.Ordinal) ? Linked(user) : null;

    /// <summary>«Отмена» while waiting: the account as it was before the link.</summary>
    public AccountState Cancel() => this with { Pending = null };

    /// <summary>Drops a code that has expired at <paramref name="now"/>.</summary>
    public AccountState Expire(DateTimeOffset now) =>
        Pending is { } pending && now >= pending.ExpiresAt ? Cancel() : this;

    /// <summary>«Отвязать»: no account and no link in progress.</summary>
    public AccountState Unlink() => Unlinked;

    /// <summary>What a check of the chat found; nothing to check without an account.</summary>
    public AccountState Checked(AccountProblem? problem) => User is null ? this : this with { Problem = problem };
}
