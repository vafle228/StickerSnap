using StickerSnap.Core.Account;

namespace StickerSnap.Tests.Core.Account;

/// <summary>The transitions of the link and what the SPA sees in each state.</summary>
public sealed class AccountStateTests
{
    private const string Computer = "DESKTOP-7F3K2Q";

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly LinkedUser Dima = new(42, "Дима", "dima");
    private static readonly PendingLink Link = new("abc", "https://t.me/StickerSnapAppBot?startapp=link_abc", Now.AddMinutes(10));

    [Fact]
    public void Waits_for_the_answer_until_the_code_expires()
    {
        var pending = AccountState.Unlinked.Start(Link);

        pending.ViewAt(Now).Should().Be(new AccountLink.Pending(Link.DeepLink, Link.ExpiresAt));
        pending.ViewAt(Link.ExpiresAt).Should().Be(new AccountLink.Unlinked(LinkEnd.Expired));
        pending.Expire(Link.ExpiresAt).ViewAt(Link.ExpiresAt).Should().Be(new AccountLink.Unlinked(LinkEnd.Expired));
        pending.Expire(Now).Should().Be(pending);
    }

    [Fact]
    public void Links_with_the_code_waiting()
    {
        var pending = AccountState.Unlinked.Start(Link);

        pending.Accept("abc", Dima, Computer).Should().Be(AccountState.Linked(Dima, Computer));
        pending.Accept("abd", Dima, Computer).Should().BeNull();
        AccountState.Unlinked.Accept("abc", Dima, Computer).Should().BeNull();
    }

    [Fact]
    public void Ends_only_the_code_waiting_and_says_how()
    {
        var pending = AccountState.Unlinked.Start(Link);

        pending.End("abc", LinkEnd.Declined).ViewAt(Now).Should().Be(new AccountLink.Unlinked(LinkEnd.Declined));
        pending.End("old", LinkEnd.Declined).Should().Be(pending);
        pending.End("abc", LinkEnd.Declined).Start(Link).Cancel().ViewAt(Now).Should().Be(new AccountLink.Unlinked());
    }

    [Fact]
    public void Shows_a_problem_of_the_linked_account_and_clears_it()
    {
        var blocked = AccountState.Linked(Dima, Computer).Checked(Dima, Computer, AccountProblem.BotBlocked);

        blocked.ViewAt(Now).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima, Computer));
        blocked.Checked(Dima, Computer, null).ViewAt(Now).Should().Be(new AccountLink.Linked(Dima, Computer));
        AccountState.Linked(Dima, Computer).Unreachable().ViewAt(Now)
            .Should().Be(new AccountLink.Failed(AccountProblem.NetworkUnavailable, Dima, Computer));
        AccountState.Unlinked.Unreachable().Should().Be(AccountState.Unlinked);
    }

    [Fact]
    public void Forgets_the_account_unlinked_from_the_phone_and_keeps_a_link_in_progress()
    {
        var relinking = AccountState.Linked(Dima, Computer).Start(Link);

        relinking.Checked(null, null, null).Should().Be(AccountState.Unlinked.Start(Link));
    }

    [Fact]
    public void Shows_a_link_in_progress_over_the_account_and_gives_it_back_on_cancel_or_decline()
    {
        var relinking = AccountState.Linked(Dima, Computer).Checked(Dima, Computer, AccountProblem.BotBlocked).Start(Link);

        relinking.ViewAt(Now).Should().BeOfType<AccountLink.Pending>();
        relinking.Cancel().ViewAt(Now).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima, Computer));
        relinking.End("abc", LinkEnd.Declined).ViewAt(Now).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima, Computer));
        relinking.Unlink().Should().Be(AccountState.Unlinked);
    }
}
