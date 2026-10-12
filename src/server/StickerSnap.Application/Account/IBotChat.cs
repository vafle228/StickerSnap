namespace StickerSnap.Application.Account;

/// <summary>
/// The chat with the product bot, @StickerSnapAppBot: how an account is linked (the user presses
/// Start with a one-time code) and how the bot answers. Messages are polled, not pushed, so the
/// desktop app needs no public address. Which client talks to the messenger is decided by the
/// registration in StickerSnap.Infrastructure.
/// </summary>
/// <remarks>Fails with the same <see cref="Core.Errors.AppException"/> codes as the sticker publisher.</remarks>
public interface IBotChat
{
    /// <summary>Who the bot is: a token of another bot would make packs with another suffix.</summary>
    Task<ChatUser> GetBotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The messages sent to the bot since <paramref name="after"/> (the <see cref="ChatMessage.Sequence"/>
    /// of the last one handled), waiting up to <paramref name="wait"/> for the first one to arrive.
    /// Asking with a later sequence tells the messenger the earlier ones are handled.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> ReceiveAsync(long? after, TimeSpan wait, CancellationToken cancellationToken);

    /// <exception cref="Core.Errors.AppException">bot_blocked when the user has stopped the bot</exception>
    Task SendAsync(long chatId, string text, CancellationToken cancellationToken);
}

/// <param name="Id">The user's id in the messenger: who owns the packs</param>
public sealed record ChatUser(long Id, string FirstName, string? LastName, string? Username);

/// <param name="Sequence">Grows with every message; the next <see cref="IBotChat.ReceiveAsync"/> asks after it</param>
/// <param name="Text">Null for a photo, a sticker or anything else that is not text</param>
public sealed record ChatMessage(long Sequence, long ChatId, ChatUser From, string? Text, DateTimeOffset SentAt);
