using StickerSnap.Application.Account;

namespace StickerSnap.Infrastructure.Telegram;

/// <summary>The chat with @StickerSnapAppBot in Telegram: getMe, long polling with getUpdates, sendMessage.</summary>
internal sealed class TelegramBotChat(BotApiClient api) : IBotChat
{
    /// <summary>The longest wait getUpdates is asked for; well within <see cref="BotApiClient.Timeout"/>.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(50);

    public async Task<ChatUser> GetBotAsync(CancellationToken cancellationToken) =>
        UserOf(await api.CallAsync<BotUser>("getMe", new { }, cancellationToken));

    public async Task<IReadOnlyList<ChatMessage>> ReceiveAsync(long? after, TimeSpan wait, CancellationToken cancellationToken)
    {
        var seconds = (int)Math.Clamp(wait.TotalSeconds, 0, MaxWait.TotalSeconds);
        var updates = await api.CallAsync<BotUpdate[]>(
            "getUpdates",
            new { Offset = after + 1, Timeout = seconds, AllowedUpdates = new[] { "message" } },
            cancellationToken);

        return
        [
            .. updates
                .Where(update => update.Message?.From is not null)
                .Select(update => new ChatMessage(
                    update.UpdateId,
                    update.Message!.Chat.Id,
                    UserOf(update.Message.From!),
                    update.Message.Text,
                    DateTimeOffset.FromUnixTimeSeconds(update.Message.Date))),
        ];
    }

    public Task SendAsync(long chatId, string text, CancellationToken cancellationToken) =>
        api.CallAsync<BotMessage>("sendMessage", new { ChatId = chatId, Text = text }, cancellationToken);

    private static ChatUser UserOf(BotUser user) => new(user.Id, user.FirstName, user.LastName, user.Username);
}
