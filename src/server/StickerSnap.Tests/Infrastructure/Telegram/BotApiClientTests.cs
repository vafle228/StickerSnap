using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Telegram;

namespace StickerSnap.Tests.Infrastructure.Telegram;

/// <summary>How a refusal, a broken answer or a missing network reaches the SPA: as a code of the contract.</summary>
public sealed class BotApiClientTests : IDisposable
{
    private readonly BotApiStub _api = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    public static TheoryData<int, string, ErrorCode> Refusals => new()
    {
        { 403, "Forbidden: bot was blocked by the user", ErrorCode.BotBlocked },
        { 400, "Bad Request: sticker set name is already occupied", ErrorCode.PackNameTaken },
        { 400, "Bad Request: STICKERS_TOO_MUCH", ErrorCode.PackFull },
        { 400, "Bad Request: STICKERSET_INVALID", ErrorCode.PackNotFound },
        { 400, "Bad Request: PEER_ID_INVALID", ErrorCode.AccountNotLinked },
        { 400, "Bad Request: user not found", ErrorCode.AccountNotLinked },
        { 403, "Forbidden: bot can't initiate conversation with a user", ErrorCode.AccountNotLinked },
        { 400, "Bad Request: STICKER_VIDEO_LONG", ErrorCode.TelegramError },
        { 401, "Unauthorized", ErrorCode.TelegramError },
        { 500, "Internal Server Error", ErrorCode.TelegramError },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Turns_a_refusal_into_its_code_with_the_words_of_telegram_in_the_log(int status, string description, ErrorCode code)
    {
        _api.Refuse("getMe", status, description);

        var error = await ErrorOf(() => _api.Client().CallAsync<BotUser>("getMe", new { }, Cancel));

        error.Code.Should().Be(code);
        error.Log.Should().Be(description);
        error.Context.Should().Contain("method", "getMe").And.Contain("answer", description);
    }

    [Fact]
    public async Task Says_how_long_to_wait_when_telegram_asks_to_slow_down()
    {
        _api.Refuse("addStickerToSet", 429, "Too Many Requests: retry after 24", retryAfter: 24);

        var error = await ErrorOf(() => _api.Client().CallAsync<bool>("addStickerToSet", new { }, Cancel));

        error.Code.Should().Be(ErrorCode.RateLimited);
        error.Params.Should().Equal(new Dictionary<string, object> { ["retryAfterSec"] = 24 });
    }

    [Fact]
    public async Task Calls_a_failed_connection_a_missing_network()
    {
        _api.Throw("getMe", new HttpRequestException("No such host is known. (bot-api.test:443)"));

        var error = await ErrorOf(() => _api.Client().CallAsync<BotUser>("getMe", new { }, Cancel));

        error.Code.Should().Be(ErrorCode.NetworkUnavailable);
        error.Log.Should().Be("No such host is known. (bot-api.test:443)");
    }

    [Fact]
    public async Task Calls_a_request_that_timed_out_a_missing_network()
    {
        _api.Throw("getMe", new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var error = await ErrorOf(() => _api.Client().CallAsync<BotUser>("getMe", new { }, Cancel));

        error.Code.Should().Be(ErrorCode.NetworkUnavailable);
    }

    [Fact]
    public async Task Lets_a_cancellation_by_the_caller_be_one()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var calling = () => _api.Client().CallAsync<BotUser>("getMe", new { }, cancelled.Token);

        await calling.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Calls_an_answer_that_is_not_the_bot_api_a_telegram_error()
    {
        _api.Answer("getMe", "<html>502 Bad Gateway</html>", System.Net.HttpStatusCode.BadGateway);

        var error = await ErrorOf(() => _api.Client().CallAsync<BotUser>("getMe", new { }, Cancel));

        error.Code.Should().Be(ErrorCode.TelegramError);
        error.Log.Should().Be("HTTP 502 without the Bot API's JSON");
    }

    [Fact]
    public async Task Sends_each_method_to_the_address_of_the_bot()
    {
        await _api.Client().CallAsync<BotUser>("getMe", new { }, Cancel);

        _api.Single("getMe").Address.Should().Be(new Uri($"https://bot-api.test/bot{BotApiStub.Token}/getMe"));
    }

    private static async Task<AppException> ErrorOf(Func<Task> call) =>
        (await call.Should().ThrowAsync<AppException>()).Which;
}
