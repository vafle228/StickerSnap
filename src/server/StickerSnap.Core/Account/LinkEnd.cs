namespace StickerSnap.Core.Account;

/// <summary>How a link ended without linking (contract, AccountLink `unlinked.reason`).</summary>
public enum LinkEnd
{
    /// <summary>The code ran out before the user answered: «Ссылка устарела».</summary>
    Expired,

    /// <summary>The user pressed «Это не я» in the messenger: «Привязку отменили в Telegram».</summary>
    Declined,
}
