using Microsoft.Extensions.Logging;
using StickerSnap.Application.Events;
using StickerSnap.Core.Account;
using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Account;

/// <summary>
/// Links this computer to the user's account through <see cref="IDeviceLinking"/>. POST
/// /account/link gives out a code in a link to the Mini App; while it waits, the answer is asked
/// for with long waits (pulled, not pushed, so the app needs no public address) until the user
/// confirms, declines or the code expires. Every change of the link is sent as accountChanged.
/// </summary>
public sealed class AccountService : IAccountService, IDisposable
{
    /// <summary>The longest wait for the answer in one request.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(25);

    /// <summary>The pause after a failed wait before the next one.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IAccountRepository _repository;
    private readonly IDeviceLinking _linking;
    private readonly IEventPublisher _events;
    private readonly TimeProvider _clock;
    private readonly ILogger<AccountService> _logger;

    /// <summary>One change of the state at a time; never held while the service is asked.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AccountState? _state;

    /// <summary>The code now waiting for the answer and the wait for it; null when nothing waits.</summary>
    private Waiting? _waiting;

    public AccountService(
        IAccountRepository repository,
        IDeviceLinking linking,
        IEventPublisher events,
        TimeProvider clock,
        ILogger<AccountService> logger)
    {
        _repository = repository;
        _linking = linking;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The wait for the answer, for tests to wait on; completed when none runs.</summary>
    internal Task Polling { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    public async Task<AccountLink> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(cancellationToken)).ViewAt(_clock.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AccountLink> StartLinkAsync(CancellationToken cancellationToken)
    {
        // Asked first: no service or no connection fails here, not after the user went to the phone
        var invite = await _linking.InviteAsync(cancellationToken);

        AccountLink view;
        Waiting? replaced;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = (await LoadAsync(cancellationToken)).Start(new PendingLink(invite.Code, invite.Link, invite.ExpiresAt));
            _state = state;
            view = state.ViewAt(_clock.GetUtcNow());
            replaced = StopWaiting();
            var waiting = new Waiting(invite);
            _waiting = waiting;
            Polling = Task.Run(() => WaitAsync(waiting), CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }

        await _events.AccountChangedAsync(view, cancellationToken);
        if (replaced is not null) await BurnAsync(replaced.Invite);
        return view;
    }

    /// <inheritdoc />
    public async Task<AccountLink> CancelOrUnlinkAsync(CancellationToken cancellationToken)
    {
        var current = await LoadLockedAsync(cancellationToken);
        // Even a code that has just expired: «Отмена» must never unlink the account behind it
        if (current.Pending is null)
        {
            // Asked first: offline the key stays good at the service, so the link stays here too
            await _linking.UnlinkAsync(cancellationToken);
        }

        AccountLink view;
        Waiting? cancelled;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            current = await LoadAsync(cancellationToken);
            if (current.Pending is not null)
            {
                _state = current.Cancel();
            }
            else
            {
                _state = current.Unlink();
                await SaveAsync(cancellationToken);
            }

            cancelled = StopWaiting();
            view = _state.ViewAt(_clock.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }

        await _events.AccountChangedAsync(view, cancellationToken);
        if (cancelled is not null) await BurnAsync(cancelled.Invite);
        return view;
    }

    /// <inheritdoc />
    public async Task<AccountLink> CheckAsync(CancellationToken cancellationToken)
    {
        var before = await LoadLockedAsync(cancellationToken);

        LinkedDevice? device;
        var reached = true;
        try
        {
            device = await _linking.GetAsync(cancellationToken);
        }
        catch (AppException exception) when (exception.Code is ErrorCode.NetworkUnavailable or ErrorCode.BotUnavailable)
        {
            // Nothing linked: no state to show it in, the request fails instead
            if (before.User is null) throw;
            device = null;
            reached = false;
        }

        AccountLink view;
        bool changed;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(cancellationToken);
            // Unlinked or linked again while the service was asked: the answer is of no one now
            var next = current.User != before.User || current.Device != before.Device
                ? current
                : reached
                    ? current.Checked(device?.User, device?.Name, device is { BotBlocked: true } ? AccountProblem.BotBlocked : null)
                    : current.Unreachable();
            changed = next != current;
            _state = next;
            if (changed) await SaveAsync(cancellationToken);
            view = next.ViewAt(_clock.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }

        if (changed) await _events.AccountChangedAsync(view, cancellationToken);
        return view;
    }

    /// <summary>The app is stopping: the wait for the answer ends.</summary>
    public void Dispose()
    {
        // Not the gate: the wait may still be on its way out through it
        StopWaiting();
    }

    /// <summary>Asks for the answer to one code until it comes, the code expires or another code replaces it.</summary>
    private async Task WaitAsync(Waiting waiting)
    {
        var stop = waiting.Stop.Token;
        try
        {
            while (await StillWaitingAsync(waiting, stop) is { } expiresAt)
            {
                var wait = expiresAt - _clock.GetUtcNow();
                if (wait > MaxWait) wait = MaxWait;

                LinkAnswer answer;
                try
                {
                    answer = await _linking.WaitAsync(waiting.Invite, wait, stop);
                }
                catch (AppException exception)
                {
                    _logger.LogWarning("The answer to the link was not read: {Code} {Message}", exception.Code, exception.Message);
                    await Task.Delay(DelayAfter(exception), _clock, stop);
                    continue;
                }

                if (answer is not LinkAnswer.Waiting)
                {
                    await AnsweredAsync(waiting, answer, stop);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Cancelled, replaced, unlinked or the app is stopping
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Waiting for the answer to the link stopped");
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (_waiting == waiting) StopWaiting();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>When the code waiting runs out; null when it is no longer waited for, after telling the SPA it expired.</summary>
    private async Task<DateTimeOffset?> StillWaitingAsync(Waiting waiting, CancellationToken stop)
    {
        AccountLink? expired = null;
        await _gate.WaitAsync(stop);
        try
        {
            if (_waiting != waiting) return null;

            var current = await LoadAsync(stop);
            var now = _clock.GetUtcNow();
            var next = current.Expire(now);
            if (next.Pending is { } pending) return pending.ExpiresAt;

            _state = next;
            if (next != current) expired = next.ViewAt(now);
            StopWaiting();
        }
        finally
        {
            _gate.Release();
        }

        if (expired is not null) await _events.AccountChangedAsync(expired, CancellationToken.None);
        return null;
    }

    private async Task AnsweredAsync(Waiting waiting, LinkAnswer answer, CancellationToken stop)
    {
        AccountLink? view = null;
        await _gate.WaitAsync(stop);
        try
        {
            if (_waiting != waiting) return;

            var current = await LoadAsync(stop);
            var code = waiting.Invite.Code;
            var next = answer switch
            {
                LinkAnswer.Linked { Device: var device } =>
                    current.Accept(code, device.User, device.Name) is { } linked
                        ? linked.Checked(device.User, device.Name, device.BotBlocked ? AccountProblem.BotBlocked : null)
                        : current,
                LinkAnswer.Declined => current.End(code, LinkEnd.Declined),
                _ => current.End(code, LinkEnd.Expired),
            };

            _state = next;
            StopWaiting();
            if (answer is LinkAnswer.Linked)
            {
                _logger.LogInformation("This computer is linked to the account {UserId}", next.User?.Id);
                // The key is already kept: the account must outlive a restart too
                await SaveAsync(CancellationToken.None);
            }

            if (next != current) view = next.ViewAt(_clock.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }

        if (view is not null) await _events.AccountChangedAsync(view, CancellationToken.None);
    }

    /// <summary>Burns a code no longer waited for, so opening its link later says it is out of date.</summary>
    private async Task BurnAsync(LinkInvite invite)
    {
        try
        {
            await _linking.CancelAsync(invite, CancellationToken.None);
        }
        catch (AppException exception)
        {
            // It runs out by itself in a few minutes; the user has moved on either way
            _logger.LogWarning("The link code was not cancelled: {Code} {Message}", exception.Code, exception.Message);
        }
    }

    /// <summary>Under the gate: the state, read from the repository the first time.</summary>
    private async Task<AccountState> LoadAsync(CancellationToken cancellationToken)
    {
        if (_state is not null) return _state;

        var record = await _repository.GetAsync(cancellationToken);
        // A record without the computer's name is from linking through the bot directly: link again
        _state = record is { User: { } user, Device: { } device }
            ? AccountState.Linked(user, device).Checked(user, device, record.BotBlocked ? AccountProblem.BotBlocked : null)
            : AccountState.Unlinked;
        return _state;
    }

    private async Task<AccountState> LoadLockedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Under the gate: what outlives a restart. A lost connection is not kept, a blocked bot is.</summary>
    private Task SaveAsync(CancellationToken cancellationToken) =>
        _repository.SaveAsync(
            new AccountRecord(_state?.User, _state?.Device, _state?.Problem == AccountProblem.BotBlocked),
            cancellationToken);

    /// <summary>Under the gate: the wait now running stops; returns it, so its code can be burned.</summary>
    private Waiting? StopWaiting()
    {
        var waiting = _waiting;
        // Not disposed: the wait may still hold its token; it has no timer to free
        waiting?.Stop.Cancel();
        _waiting = null;
        return waiting;
    }

    private static TimeSpan DelayAfter(AppException exception) =>
        exception.Code == ErrorCode.RateLimited && exception.Params.TryGetValue("retryAfterSec", out var seconds)
            ? TimeSpan.FromSeconds(Convert.ToDouble(seconds, System.Globalization.CultureInfo.InvariantCulture))
            : RetryDelay;

    /// <summary>A code waiting for the answer and what stops the wait for it.</summary>
    private sealed class Waiting(LinkInvite invite)
    {
        public LinkInvite Invite { get; } = invite;

        public CancellationTokenSource Stop { get; } = new();
    }
}
