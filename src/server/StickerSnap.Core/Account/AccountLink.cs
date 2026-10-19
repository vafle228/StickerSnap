namespace StickerSnap.Core.Account;

/// <summary>The four states of the link the SPA shows (contract, AccountLink; artboards 5 and 18).</summary>
public abstract record AccountLink
{
    private AccountLink()
    {
    }

    /// <summary>`state: unlinked`: publishing needs linking first; <paramref name="Reason"/> says how the last link ended.</summary>
    public sealed record Unlinked(LinkEnd? Reason = null) : AccountLink;

    /// <summary>`state: pending`: the link is given out, the user has not answered in the messenger yet.</summary>
    public sealed record Pending(string DeepLink, DateTimeOffset ExpiresAt) : AccountLink;

    /// <summary>`state: linked`: this computer, <paramref name="Device"/>, publishes for <paramref name="User"/>.</summary>
    public sealed record Linked(LinkedUser User, string Device) : AccountLink;

    /// <summary>`state: error`: linked, yet the bot cannot act for <paramref name="User"/> now.</summary>
    public sealed record Failed(AccountProblem Problem, LinkedUser User, string Device) : AccountLink;
}
