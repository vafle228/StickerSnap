using Microsoft.Extensions.Logging.Abstractions;
using StickerSnap.Application.Account;
using StickerSnap.Application.Events;
using StickerSnap.Core.Account;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Application.Account;

/// <summary>
/// unlinked → pending → linked with a fake bot chat and the test's clock: Start with the code,
/// another code, an expired one, «Отмена», «Отвязать», «Проверить связь».
/// </summary>
public sealed class AccountServiceTests : IDisposable
{
    private static readonly LinkedUser Dima = new(42, "Дима", "dima");

    private readonly WatchedClock _clock = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly IAccountRepository _repository = Substitute.For<IAccountRepository>();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();
    private readonly FakeBotChat _chat;
    private readonly AccountService _service;
    private long _sequence = 815000000;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public AccountServiceTests()
    {
        _chat = new FakeBotChat(_clock);
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(AccountRecord.None);
        _service = new AccountService(_repository, _chat, _events, _clock, NullLogger<AccountService>.Instance);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Is_unlinked_without_a_saved_account()
    {
        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Unlinked());
    }

    [Fact]
    public async Task Gives_the_saved_account_and_a_blocked_bot_found_before()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, BotBlocked: true, 7));

        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima));
    }

    [Fact]
    public async Task Gives_out_a_link_to_the_bot_with_a_one_time_code()
    {
        var first = (AccountLink.Pending)await _service.StartLinkAsync(Cancel);
        var second = (AccountLink.Pending)await _service.StartLinkAsync(Cancel);

        first.DeepLink.Should().MatchRegex("^https://t.me/StickerSnapAppBot\\?start=[A-Za-z0-9]{24}$");
        first.ExpiresAt.Should().Be(_clock.GetUtcNow() + AccountService.CodeLifetime);
        second.DeepLink.Should().NotBe(first.DeepLink);
        await _events.Received(1).AccountChangedAsync(first, Arg.Any<CancellationToken>());
        (await _service.GetAsync(Cancel)).Should().Be(second);
    }

    [Fact]
    public async Task Links_whoever_presses_Start_with_the_code_and_greets_them()
    {
        var code = CodeOf(await _service.StartLinkAsync(Cancel));
        await _chat.ParkedAsync();

        var start = Message($"/start {code}");
        _chat.Deliver(start);
        await _service.Polling;

        var linked = new AccountLink.Linked(Dima);
        (await _service.GetAsync(Cancel)).Should().Be(linked);
        await _events.Received(1).AccountChangedAsync(linked, Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveAsync(new AccountRecord(Dima, false, start.Sequence), Arg.Any<CancellationToken>());
        _chat.Sent.Should().Equal((Dima.Id, AccountService.Greeting));
    }

    [Fact]
    public async Task Answers_another_code_or_none_and_waits_on()
    {
        var code = CodeOf(await _service.StartLinkAsync(Cancel));
        await _chat.ParkedAsync();

        _chat.Deliver(Message("/start a1b2c3"), Message("/start"), Message("привет"));
        await _chat.ParkedAsync(2);

        (await _service.GetAsync(Cancel)).Should().BeOfType<AccountLink.Pending>();
        _chat.Sent.Should().Equal((Dima.Id, AccountService.Outdated), (Dima.Id, AccountService.Hint));

        _chat.Deliver(Message($"/start {code}"));
        await _service.Polling;

        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Linked(Dima));
    }

    [Fact]
    public async Task Does_not_take_a_code_given_out_before_the_new_one()
    {
        var old = CodeOf(await _service.StartLinkAsync(Cancel));
        await _service.StartLinkAsync(Cancel);
        await _chat.ParkedAsync();

        _chat.Deliver(Message($"/start {old}"));
        await _chat.ParkedAsync(2);

        (await _service.GetAsync(Cancel)).Should().BeOfType<AccountLink.Pending>();
        _chat.Sent.Should().Equal((Dima.Id, AccountService.Outdated));
    }

    [Fact]
    public async Task Goes_back_to_unlinked_when_the_code_expires()
    {
        await _service.StartLinkAsync(Cancel);
        await _chat.ParkedAsync();

        _clock.Advance(AccountService.CodeLifetime);
        await _service.Polling;

        var unlinked = new AccountLink.Unlinked();
        (await _service.GetAsync(Cancel)).Should().Be(unlinked);
        await _events.Received(1).AccountChangedAsync(unlinked, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Waits_no_longer_than_the_code_lives()
    {
        await _service.StartLinkAsync(Cancel);
        await _chat.ParkedAsync();

        // Each read waits at most MaxWait; the last ends with the code
        _clock.Advance(AccountService.CodeLifetime - TimeSpan.FromSeconds(10));
        await _chat.ParkedAsync(2);
        _clock.Advance(TimeSpan.FromSeconds(10));
        await _service.Polling;

        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Unlinked());
    }

    [Fact]
    public async Task Reads_on_from_the_last_message_handled_before_a_restart()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(null, false, 815000000));
        await _service.StartLinkAsync(Cancel);
        await _chat.ParkedAsync();

        var hello = Message("привет");
        _chat.Deliver(hello);
        await _chat.ParkedAsync(2);

        _chat.Reads.Should().Equal(815000000, hello.Sequence);
        await _repository.Received().SaveAsync(new AccountRecord(null, false, hello.Sequence), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reads_again_after_a_failed_read()
    {
        _chat.FailNextRead(new AppException(ErrorCode.NetworkUnavailable, "offline"));
        await _service.StartLinkAsync(Cancel);

        // Only once the pause has begun: an advance before it would let the code expire instead
        await _clock.TimerStartedAsync(AccountService.RetryDelay);
        _chat.Reads.Should().ContainSingle();

        _clock.Advance(AccountService.RetryDelay);
        await _chat.ParkedAsync();

        _chat.Reads.Should().Equal(new long?[] { null, null });
    }

    [Fact]
    public async Task Cancel_while_waiting_gives_back_the_account_linked_before()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, BotBlocked: true, null));
        await _service.StartLinkAsync(Cancel);
        await _chat.ParkedAsync();

        var link = await _service.CancelOrUnlinkAsync(Cancel);
        await _service.Polling;

        link.Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima));
        await _events.Received(1).AccountChangedAsync(link, Arg.Any<CancellationToken>());
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
    }

    [Fact]
    public async Task Cancel_after_the_code_expired_still_keeps_the_account()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, false, null));
        await _service.StartLinkAsync(Cancel);
        _service.Dispose();
        await _service.Polling;
        _clock.Advance(AccountService.CodeLifetime);

        (await _service.CancelOrUnlinkAsync(Cancel)).Should().Be(new AccountLink.Linked(Dima));
    }

    [Fact]
    public async Task Unlink_forgets_the_account()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, false, 9));

        var link = await _service.CancelOrUnlinkAsync(Cancel);

        link.Should().Be(new AccountLink.Unlinked());
        await _repository.Received(1).SaveAsync(new AccountRecord(null, false, 9), Arg.Any<CancellationToken>());
        await _events.Received(1).AccountChangedAsync(link, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gives_no_link_without_a_bot_token()
    {
        _chat.BotFailure = new AppException(ErrorCode.BotUnavailable, "no token");

        var starting = () => _service.StartLinkAsync(Cancel);

        (await starting.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.BotUnavailable);
        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Unlinked());
        _chat.Reads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ErrorCode.BotBlocked, AccountProblem.BotBlocked, true)]
    [InlineData(ErrorCode.AccountNotLinked, AccountProblem.BotBlocked, true)]
    [InlineData(ErrorCode.NetworkUnavailable, AccountProblem.NetworkUnavailable, false)]
    public async Task Check_finds_what_keeps_the_bot_from_the_user(ErrorCode code, AccountProblem problem, bool kept)
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, false, 3));
        _chat.ChatFailure = new AppException(code, "refused");

        var link = await _service.CheckAsync(Cancel);

        link.Should().Be(new AccountLink.Failed(problem, Dima));
        await _repository.Received(1).SaveAsync(new AccountRecord(Dima, kept, 3), Arg.Any<CancellationToken>());
        await _events.Received(1).AccountChangedAsync(link, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_clears_a_problem_once_the_bot_reaches_the_user_again()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, true, null));

        var link = await _service.CheckAsync(Cancel);

        link.Should().Be(new AccountLink.Linked(Dima));
        await _repository.Received(1).SaveAsync(new AccountRecord(Dima, false, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_of_a_healthy_link_changes_and_announces_nothing()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, false, null));

        (await _service.CheckAsync(Cancel)).Should().Be(new AccountLink.Linked(Dima));

        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
        await _events.DidNotReceiveWithAnyArgs().AccountChangedAsync(default!, Cancel);
    }

    [Fact]
    public async Task Check_without_an_account_fails_when_the_bot_is_out_of_reach()
    {
        _chat.BotFailure = new AppException(ErrorCode.NetworkUnavailable, "offline");

        var checking = () => _service.CheckAsync(Cancel);

        (await checking.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.NetworkUnavailable);
    }

    [Theory]
    [InlineData("/start abc", "abc")]
    [InlineData("/start  abc ", "abc")]
    [InlineData("/start@StickerSnapAppBot abc", "abc")]
    [InlineData("/start", "")]
    [InlineData("/started abc", null)]
    [InlineData("привет", null)]
    [InlineData(null, null)]
    public void Takes_the_code_from_Start(string? text, string? code)
    {
        AccountService.CodeOf(text).Should().Be(code);
    }

    private ChatMessage Message(string text) =>
        new(++_sequence, Dima.Id, new ChatUser(Dima.Id, Dima.FirstName, "Дмитриев", Dima.Username), text, _clock.GetUtcNow());

    private static string CodeOf(AccountLink link) => ((AccountLink.Pending)link).DeepLink.Split("start=")[1];
}
