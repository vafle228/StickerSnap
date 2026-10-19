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

/// <param name="User">Whom this computer publishes for, if it is linked</param>
/// <param name="Device">This computer's name in the user's devices; a record without it is not linked</param>
/// <param name="BotBlocked">The last check found the bot blocked; a lost connection is not kept</param>
public sealed record AccountRecord(LinkedUser? User, string? Device, bool BotBlocked)
{
    public static readonly AccountRecord None = new(null, null, false);
}
