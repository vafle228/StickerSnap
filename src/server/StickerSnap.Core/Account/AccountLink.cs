namespace StickerSnap.Core.Account;

/// <summary>The four states of the link the SPA shows (contract, AccountLink; artboard 5).</summary>
public abstract record AccountLink
{
    private AccountLink()
    {
    }

    /// <summary>`state: unlinked`: publishing needs the bot first.</summary>
    public sealed record Unlinked : AccountLink;

    /// <summary>`state: pending`: the bot was opened with a code, Start not pressed yet.</summary>
    public sealed record Pending(string DeepLink, DateTimeOffset ExpiresAt) : AccountLink;

    /// <summary>`state: linked`: packs are made on <paramref name="User"/>'s behalf.</summary>
    public sealed record Linked(LinkedUser User) : AccountLink;

    /// <summary>`state: error`: linked, yet the bot cannot act for <paramref name="User"/> now.</summary>
    public sealed record Failed(AccountProblem Problem, LinkedUser User) : AccountLink;
}
