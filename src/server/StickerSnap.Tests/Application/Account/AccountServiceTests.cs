using Microsoft.Extensions.Logging.Abstractions;
using StickerSnap.Application.Account;
using StickerSnap.Application.Events;
using StickerSnap.Core.Account;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Application.Account;

/// <summary>
/// unlinked → pending → linked with a fake linking service and the test's clock: «Привязать» and
/// «Это не я» in the Mini App, an expired code, «Отмена», «Отвязать», «Проверить связь».
/// </summary>
public sealed class AccountServiceTests : IDisposable
{
    private const string Computer = "DESKTOP-7F3K2Q";

    private static readonly LinkedUser Dima = new(42, "Дима", "dima");

    private readonly WatchedClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly IAccountRepository _repository = Substitute.For<IAccountRepository>();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();
    private readonly FakeDeviceLinking _linking;
    private readonly AccountService _service;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public AccountServiceTests()
    {
        _linking = new FakeDeviceLinking(_clock);
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(AccountRecord.None);
        _service = new AccountService(_repository, _linking, _events, _clock, NullLogger<AccountService>.Instance);
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
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, BotBlocked: true));

        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima, Computer));
    }

    [Fact]
    public async Task Takes_an_account_saved_without_the_computer_for_none()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, null, false));

        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Unlinked());
    }

    [Fact]
    public async Task Gives_out_the_link_to_the_Mini_App_and_burns_the_code_it_replaces()
    {
        var first = (AccountLink.Pending)await _service.StartLinkAsync(Cancel);
        var second = (AccountLink.Pending)await _service.StartLinkAsync(Cancel);

        first.Should().Be(new AccountLink.Pending(_linking.Invites[0].Link, _clock.GetUtcNow() + FakeDeviceLinking.CodeLifetime));
        second.DeepLink.Should().Be(_linking.Invites[1].Link);
        _linking.Cancelled.Should().Equal(_linking.Invites[0]);
        await _events.Received(1).AccountChangedAsync(first, Arg.Any<CancellationToken>());
        (await _service.GetAsync(Cancel)).Should().Be(second);
    }

    [Fact]
    public async Task Links_once_the_user_confirms_in_the_Mini_App()
    {
        await _service.StartLinkAsync(Cancel);
        await _linking.ParkedAsync();

        _linking.Answer(new LinkAnswer.Linked(Device()));
        await _service.Polling;

        var linked = new AccountLink.Linked(Dima, Computer);
        (await _service.GetAsync(Cancel)).Should().Be(linked);
        await _events.Received(1).AccountChangedAsync(linked, Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveAsync(new AccountRecord(Dima, Computer, false), Arg.Any<CancellationToken>());
        _linking.Cancelled.Should().BeEmpty();
    }

    [Fact]
    public async Task Links_a_user_who_has_stopped_the_bot_with_the_problem_shown()
    {
        await _service.StartLinkAsync(Cancel);
        _linking.Answer(new LinkAnswer.Linked(Device(botBlocked: true)));
        await _service.Polling;

        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima, Computer));
        await _repository.Received(1).SaveAsync(new AccountRecord(Dima, Computer, true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Goes_back_with_the_reason_when_the_user_declines()
    {
        await _service.StartLinkAsync(Cancel);
        await _linking.ParkedAsync();

        _linking.Answer(new LinkAnswer.Declined());
        await _service.Polling;

        var declined = new AccountLink.Unlinked(LinkEnd.Declined);
        (await _service.GetAsync(Cancel)).Should().Be(declined);
        await _events.Received(1).AccountChangedAsync(declined, Arg.Any<CancellationToken>());
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
    }

    [Fact]
    public async Task A_decline_gives_back_the_account_linked_before()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, BotBlocked: true));
        await _service.StartLinkAsync(Cancel);

        _linking.Answer(new LinkAnswer.Declined());
        await _service.Polling;

        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima, Computer));
    }

    [Fact]
    public async Task Asks_again_while_the_user_has_not_answered()
    {
        await _service.StartLinkAsync(Cancel);
        await _linking.ParkedAsync();

        _clock.Advance(AccountService.MaxWait);
        await _linking.ParkedAsync(2);

        (await _service.GetAsync(Cancel)).Should().BeOfType<AccountLink.Pending>();
        _linking.Waits.Should().HaveCount(2);
    }

    [Fact]
    public async Task Goes_back_to_unlinked_when_the_code_expires()
    {
        await _service.StartLinkAsync(Cancel);
        await _linking.ParkedAsync();

        // Each wait lasts at most MaxWait; the last ends with the code
        _clock.Advance(FakeDeviceLinking.CodeLifetime - TimeSpan.FromSeconds(10));
        await _linking.ParkedAsync(2);
        _clock.Advance(TimeSpan.FromSeconds(10));
        await _service.Polling;

        var expired = new AccountLink.Unlinked(LinkEnd.Expired);
        (await _service.GetAsync(Cancel)).Should().Be(expired);
        await _events.Received(1).AccountChangedAsync(expired, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Asks_again_after_a_failed_wait()
    {
        _linking.FailNextWait(new AppException(ErrorCode.NetworkUnavailable, "offline"));
        await _service.StartLinkAsync(Cancel);

        // Only once the pause has begun: an advance before it would let the code expire instead
        await _clock.TimerStartedAsync(AccountService.RetryDelay);
        _linking.Waits.Should().ContainSingle();

        _clock.Advance(AccountService.RetryDelay);
        await _linking.ParkedAsync();

        _linking.Waits.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_new_link_clears_the_reason_of_the_last_one()
    {
        await _service.StartLinkAsync(Cancel);
        _linking.Answer(new LinkAnswer.Declined());
        await _service.Polling;

        (await _service.StartLinkAsync(Cancel)).Should().BeOfType<AccountLink.Pending>();
        (await _service.CancelOrUnlinkAsync(Cancel)).Should().Be(new AccountLink.Unlinked());
    }

    [Fact]
    public async Task Cancel_while_waiting_burns_the_code_and_gives_back_the_account_linked_before()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, BotBlocked: true));
        await _service.StartLinkAsync(Cancel);
        await _linking.ParkedAsync();

        var link = await _service.CancelOrUnlinkAsync(Cancel);
        await _service.Polling;

        link.Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima, Computer));
        _linking.Cancelled.Should().Equal(_linking.Invites[0]);
        _linking.Unlinked.Should().Be(0);
        await _events.Received(1).AccountChangedAsync(link, Arg.Any<CancellationToken>());
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
    }

    [Fact]
    public async Task Cancel_after_the_code_expired_still_keeps_the_account()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, false));
        await _service.StartLinkAsync(Cancel);
        _service.Dispose();
        await _service.Polling;
        _clock.Advance(FakeDeviceLinking.CodeLifetime);

        (await _service.CancelOrUnlinkAsync(Cancel)).Should().Be(new AccountLink.Linked(Dima, Computer));
        _linking.Unlinked.Should().Be(0);
    }

    [Fact]
    public async Task Unlink_unlinks_the_computer_at_the_service_and_forgets_the_account()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, false));

        var link = await _service.CancelOrUnlinkAsync(Cancel);

        link.Should().Be(new AccountLink.Unlinked());
        _linking.Unlinked.Should().Be(1);
        await _repository.Received(1).SaveAsync(AccountRecord.None, Arg.Any<CancellationToken>());
        await _events.Received(1).AccountChangedAsync(link, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unlink_offline_keeps_the_link()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, false));
        _linking.DeviceFailure = new AppException(ErrorCode.NetworkUnavailable, "offline");

        var unlinking = () => _service.CancelOrUnlinkAsync(Cancel);

        (await unlinking.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.NetworkUnavailable);
        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Linked(Dima, Computer));
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
    }

    [Fact]
    public async Task Gives_no_link_without_the_service()
    {
        _linking.InviteFailure = new AppException(ErrorCode.BotUnavailable, "no service");

        var starting = () => _service.StartLinkAsync(Cancel);

        (await starting.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.BotUnavailable);
        (await _service.GetAsync(Cancel)).Should().Be(new AccountLink.Unlinked());
        _linking.Waits.Should().BeEmpty();
    }

    [Fact]
    public async Task Check_finds_the_bot_stopped_and_keeps_it()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, false));
        _linking.Device = Device(botBlocked: true);

        var link = await _service.CheckAsync(Cancel);

        link.Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima, Computer));
        await _repository.Received(1).SaveAsync(new AccountRecord(Dima, Computer, true), Arg.Any<CancellationToken>());
        await _events.Received(1).AccountChangedAsync(link, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ErrorCode.NetworkUnavailable)]
    [InlineData(ErrorCode.BotUnavailable)]
    public async Task Check_shows_the_service_out_of_reach_without_keeping_it(ErrorCode code)
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, false));
        _linking.DeviceFailure = new AppException(code, "offline");

        var link = await _service.CheckAsync(Cancel);

        link.Should().Be(new AccountLink.Failed(AccountProblem.NetworkUnavailable, Dima, Computer));
        await _repository.Received(1).SaveAsync(new AccountRecord(Dima, Computer, false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_finds_the_computer_unlinked_from_the_phone()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, false));
        _linking.Device = null;

        var link = await _service.CheckAsync(Cancel);

        link.Should().Be(new AccountLink.Unlinked());
        await _repository.Received(1).SaveAsync(AccountRecord.None, Arg.Any<CancellationToken>());
        await _events.Received(1).AccountChangedAsync(link, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_clears_a_problem_once_the_bot_reaches_the_user_again()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, true));
        _linking.Device = Device();

        var link = await _service.CheckAsync(Cancel);

        link.Should().Be(new AccountLink.Linked(Dima, Computer));
        await _repository.Received(1).SaveAsync(new AccountRecord(Dima, Computer, false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_takes_a_new_name_of_the_computer()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, false));
        _linking.Device = Device() with { Name = "Ноутбук" };

        (await _service.CheckAsync(Cancel)).Should().Be(new AccountLink.Linked(Dima, "Ноутбук"));
    }

    [Fact]
    public async Task Check_of_a_healthy_link_changes_and_announces_nothing()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountRecord(Dima, Computer, false));
        _linking.Device = Device();

        (await _service.CheckAsync(Cancel)).Should().Be(new AccountLink.Linked(Dima, Computer));

        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
        await _events.DidNotReceiveWithAnyArgs().AccountChangedAsync(default!, Cancel);
    }

    [Fact]
    public async Task Check_without_an_account_fails_when_the_service_is_out_of_reach()
    {
        _linking.DeviceFailure = new AppException(ErrorCode.NetworkUnavailable, "offline");

        var checking = () => _service.CheckAsync(Cancel);

        (await checking.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.NetworkUnavailable);
    }

    private static LinkedDevice Device(bool botBlocked = false) =>
        new("dev_1", Computer, new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero), Dima, botBlocked);
}
