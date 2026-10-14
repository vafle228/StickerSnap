using StickerSnap.Core.Account;

namespace StickerSnap.Tests.Core.Account;

/// <summary>The transitions of the link and what the SPA sees in each state.</summary>
public sealed class AccountStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly LinkedUser Dima = new(42, "Дима", "dima");
    private static readonly PendingLink Link = new("abc", "https://t.me/StickerSnapAppBot?start=abc", Now.AddMinutes(10));

    [Fact]
    public void Waits_for_Start_until_the_code_expires()
    {
        var pending = AccountState.Unlinked.Start(Link);

        pending.ViewAt(Now).Should().Be(new AccountLink.Pending(Link.DeepLink, Link.ExpiresAt));
        pending.ViewAt(Link.ExpiresAt).Should().Be(new AccountLink.Unlinked());
        pending.Expire(Link.ExpiresAt).Should().Be(AccountState.Unlinked);
        pending.Expire(Now).Should().Be(pending);
    }

    [Fact]
    public void Links_with_the_code_while_it_lives()
    {
        var pending = AccountState.Unlinked.Start(Link);

        pending.Accept("abc", Dima, Now).Should().Be(AccountState.Linked(Dima));
        pending.Accept("abd", Dima, Now).Should().BeNull();
        pending.Accept("abc", Dima, Link.ExpiresAt).Should().BeNull();
        AccountState.Unlinked.Accept("abc", Dima, Now).Should().BeNull();
    }

    [Fact]
    public void Shows_a_problem_of_the_linked_account_and_clears_it()
    {
        var blocked = AccountState.Linked(Dima).Checked(AccountProblem.BotBlocked);

        blocked.ViewAt(Now).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima));
        blocked.Checked(null).ViewAt(Now).Should().Be(new AccountLink.Linked(Dima));
        AccountState.Unlinked.Checked(AccountProblem.NetworkUnavailable).Should().Be(AccountState.Unlinked);
    }

    [Fact]
    public void Shows_a_link_in_progress_over_the_account_and_gives_it_back_on_cancel()
    {
        var relinking = AccountState.Linked(Dima).Checked(AccountProblem.BotBlocked).Start(Link);

        relinking.ViewAt(Now).Should().BeOfType<AccountLink.Pending>();
        relinking.Cancel().ViewAt(Now).Should().Be(new AccountLink.Failed(AccountProblem.BotBlocked, Dima));
        relinking.Unlink().Should().Be(AccountState.Unlinked);
    }
}
