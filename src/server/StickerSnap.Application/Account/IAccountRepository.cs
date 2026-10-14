using StickerSnap.Core.Account;

namespace StickerSnap.Application.Account;

/// <summary>Where the linked account is kept between launches; a link in progress is not.</summary>
public interface IAccountRepository
{
    /// <summary>The saved account, or none when nothing is saved or the saved file is broken.</summary>
    Task<AccountRecord> GetAsync(CancellationToken cancellationToken);

    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task SaveAsync(AccountRecord account, CancellationToken cancellationToken);
}

/// <param name="User">The linked account, if any</param>
/// <param name="BotBlocked">The last check found the bot blocked; a lost connection is not kept</param>
/// <param name="LastMessage">The sequence of the last message to the bot handled, so a restart does not read it again</param>
public sealed record AccountRecord(LinkedUser? User, bool BotBlocked, long? LastMessage)
{
    public static readonly AccountRecord None = new(null, false, null);
}
