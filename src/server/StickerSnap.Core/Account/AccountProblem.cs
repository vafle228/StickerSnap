namespace StickerSnap.Core.Account;

/// <summary>Why a linked account cannot publish now (contract, AccountLink `state: error`).</summary>
public enum AccountProblem
{
    /// <summary>The user stopped or blocked the bot: starting it again fixes it.</summary>
    BotBlocked,

    /// <summary>The service is out of reach: the link stays, a later check clears it.</summary>
    NetworkUnavailable,
}
