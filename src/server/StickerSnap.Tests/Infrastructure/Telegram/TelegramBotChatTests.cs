using StickerSnap.Application.Account;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Telegram;

namespace StickerSnap.Tests.Infrastructure.Telegram;

/// <summary>getMe, getUpdates and sendMessage, against answers taken from the Bot API.</summary>
public sealed class TelegramBotChatTests : IDisposable
{
    private readonly BotApiStub _api = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private TelegramBotChat Chat => new(_api.Client());

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Knows_which_bot_the_token_is_of()
    {
        var bot = await Chat.GetBotAsync(Cancel);

        bot.Should().Be(new ChatUser(7000000001, "StickerSnap", null, "StickerSnapAppBot"));
    }

    [Fact]
    public async Task Receives_the_messages_sent_to_the_bot()
    {
        var messages = await Chat.ReceiveAsync(after: null, TimeSpan.FromSeconds(30), Cancel);

        messages.Should().BeEquivalentTo(
            [
                new ChatMessage(815000001, 42, new ChatUser(42, "Дима", "С", "dima"), "/start a1b2c3", DateTimeOffset.FromUnixTimeSeconds(1791799200)),
                new ChatMessage(815000002, 42, new ChatUser(42, "Дима", null, null), null, DateTimeOffset.FromUnixTimeSeconds(1791799260)),
            ]);
        _api.Single("getUpdates").Body.Should().Be("{\"timeout\":30,\"allowed_updates\":[\"message\"]}");
    }

    [Fact]
    public async Task Asks_for_the_messages_after_the_last_one_handled_and_waits_no_longer_than_the_client()
    {
        await Chat.ReceiveAsync(after: 815000002, TimeSpan.FromMinutes(5), Cancel);

        _api.Single("getUpdates").Body.Should().Be("{\"offset\":815000003,\"timeout\":50,\"allowed_updates\":[\"message\"]}");
    }

    [Fact]
    public async Task Answers_in_the_chat()
    {
        _api.Answer("sendMessage", """{"ok":true,"result":{"message_id":13,"chat":{"id":42},"date":1791799300,"text":"Готово"}}""");

        await Chat.SendAsync(42, "Готово", Cancel);

        _api.Single("sendMessage").Body.Should().Be("{\"chat_id\":42,\"text\":\"Готово\"}");
    }

    [Fact]
    public async Task Says_the_user_has_blocked_the_bot()
    {
        _api.Refuse("sendMessage", 403, "Forbidden: bot was blocked by the user");

        var sending = () => Chat.SendAsync(42, "Готово", Cancel);

        (await sending.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.BotBlocked);
    }
}
