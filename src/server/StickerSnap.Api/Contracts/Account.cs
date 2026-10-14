using System.Text.Json.Serialization;
using StickerSnap.Core.Account;

namespace StickerSnap.Api.Contracts;

/// <summary>The four states of artboard 5 (schema AccountLink), told apart by `state`.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "state")]
[JsonDerivedType(typeof(UnlinkedAccount), "unlinked")]
[JsonDerivedType(typeof(PendingAccount), "pending")]
[JsonDerivedType(typeof(LinkedAccount), "linked")]
[JsonDerivedType(typeof(FailedAccount), "error")]
public abstract record AccountLink
{
    public static AccountLink From(Core.Account.AccountLink link) => link switch
    {
        Core.Account.AccountLink.Unlinked => new UnlinkedAccount(),
        Core.Account.AccountLink.Pending pending => new PendingAccount(pending.DeepLink, pending.ExpiresAt),
        Core.Account.AccountLink.Linked linked => new LinkedAccount(TelegramUser.From(linked.User)),
        Core.Account.AccountLink.Failed failed => new FailedAccount(failed.Problem, TelegramUser.From(failed.User)),
        _ => throw new ArgumentOutOfRangeException(nameof(link), link, "A state of the link the contract does not know"),
    };
}

public sealed record UnlinkedAccount : AccountLink;

/// <param name="DeepLink">https://t.me/StickerSnapAppBot?start=&lt;code&gt;</param>
public sealed record PendingAccount(string DeepLink, DateTimeOffset ExpiresAt) : AccountLink;

public sealed record LinkedAccount(TelegramUser User) : AccountLink;

/// <param name="Code">`bot_blocked` or `network_unavailable`</param>
public sealed record FailedAccount(AccountProblem Code, TelegramUser? User) : AccountLink;

/// <summary>Who is linked (schema TelegramUser).</summary>
public sealed record TelegramUser(long Id, string? Username, string FirstName)
{
    public static TelegramUser From(LinkedUser user) => new(user.Id, user.Username, user.FirstName);
}
