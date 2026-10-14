using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using StickerSnap.Application.Events;
using StickerSnap.Core.Account;
using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Account;

/// <summary>
/// Links the user's account to the product bot. POST /account/link gives out a one-time code in
/// a link to the bot; while it waits, the bot's messages are read (polled, not pushed, so the app
/// needs no public address) until `/start &lt;code&gt;` arrives or the code expires. Start with
/// the code links the sender and the bot greets them; Start with another code, or none, gets a
/// hint. Every change of the link is sent as accountChanged.
/// </summary>
public sealed class AccountService : IAccountService, IDisposable
{
    /// <summary>How long a code links: enough to open the messenger and press Start.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The longest wait for the bot's messages in one request.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(25);

    /// <summary>The pause after a failed read before the next one.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    public const string Greeting =
        "Готово, аккаунт привязан к StickerSnap. Возвращайтесь в приложение: паки будут создаваться на вас.";

    public const string Outdated =
        "Эта ссылка устарела. Откройте бота заново из StickerSnap: «Настройки» → «Публикация в Telegram».";

    public const string Hint =
        "Чтобы привязать аккаунт, откройте бота из StickerSnap: «Настройки» → «Публикация в Telegram».";

    private const string StartCommand = "/start";

    private readonly IAccountRepository _repository;
    private readonly IBotChat _chat;
    private readonly IEventPublisher _events;
    private readonly TimeProvider _clock;
    private readonly ILogger<AccountService> _logger;

    /// <summary>One change of the state at a time; never held while the messenger is asked.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AccountState? _state;
    private long? _lastMessage;

    /// <summary>The reading of the bot's messages now running; null when nothing waits for Start.</summary>
    private CancellationTokenSource? _polling;

    public AccountService(
        IAccountRepository repository,
        IBotChat chat,
        IEventPublisher events,
        TimeProvider clock,
        ILogger<AccountService> logger)
    {
        _repository = repository;
        _chat = chat;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The reading of the bot's messages, for tests to wait on; completed when none runs.</summary>
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
        // Asked first: no token or no connection fails here, not after the user went to the bot
        var bot = await _chat.GetBotAsync(cancellationToken);
        if (bot.Username is not { Length: > 0 } username)
            throw new AppException(ErrorCode.TelegramError, "The bot has no username to link to") { Log = "getMe gave no username" };

        var code = NewCode();
        var link = new PendingLink(code, $"https://t.me/{username}?start={code}", _clock.GetUtcNow() + CodeLifetime);

        AccountLink view;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = (await LoadAsync(cancellationToken)).Start(link);
            _state = state;
            view = state.ViewAt(_clock.GetUtcNow());
            if (_polling is null)
            {
                _polling = new CancellationTokenSource();
                var polling = _polling;
                Polling = Task.Run(() => PollAsync(polling), CancellationToken.None);
            }
        }
        finally
        {
            _gate.Release();
        }

        await _events.AccountChangedAsync(view, cancellationToken);
        return view;
    }

    /// <inheritdoc />
    public async Task<AccountLink> CancelOrUnlinkAsync(CancellationToken cancellationToken)
    {
        AccountLink view;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(cancellationToken);
            var now = _clock.GetUtcNow();
            // Even a code that has just expired: «Отмена» must never unlink the account behind it
            if (current.Pending is not null)
            {
                _state = current.Cancel();
            }
            else
            {
                _state = current.Unlink();
                await SaveAsync(cancellationToken);
            }

            StopPolling();
            view = _state.ViewAt(now);
        }
        finally
        {
            _gate.Release();
        }

        await _events.AccountChangedAsync(view, cancellationToken);
        return view;
    }

    /// <inheritdoc />
    public async Task<AccountLink> CheckAsync(CancellationToken cancellationToken)
    {
        var user = (await LoadLockedAsync(cancellationToken)).User;
        if (user is null)
        {
            // Nothing to check but the bot itself: no connection is an error of the request
            await _chat.GetBotAsync(cancellationToken);
            return await GetAsync(cancellationToken);
        }

        AccountProblem? problem;
        try
        {
            await _chat.CheckChatAsync(user.Id, cancellationToken);
            problem = null;
        }
        catch (AppException exception) when (exception.Code is ErrorCode.BotBlocked or ErrorCode.AccountNotLinked)
        {
            problem = AccountProblem.BotBlocked;
        }
        catch (AppException exception) when (exception.Code is ErrorCode.NetworkUnavailable)
        {
            problem = AccountProblem.NetworkUnavailable;
        }

        AccountLink view;
        bool changed;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(cancellationToken);
            // Unlinked or linked again while the messenger was asked: the answer is of no one now
            var next = current.User == user ? current.Checked(problem) : current;
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

    /// <summary>The app is stopping: the reading of the bot's messages ends.</summary>
    public void Dispose()
    {
        // Not the gate: the reading may still be on its way out through it
        StopPolling();
    }

    /// <summary>Reads the bot's messages while a code waits for Start; ends when it is used, expires or is cancelled.</summary>
    private async Task PollAsync(CancellationTokenSource polling)
    {
        var stop = polling.Token;
        try
        {
            while (true)
            {
                var (pending, after) = await NextWaitAsync(polling, stop);
                if (pending is null) return;

                var wait = pending.ExpiresAt - _clock.GetUtcNow();
                if (wait > MaxWait) wait = MaxWait;

                IReadOnlyList<ChatMessage> messages;
                try
                {
                    messages = await _chat.ReceiveAsync(after, wait, stop);
                }
                catch (AppException exception)
                {
                    _logger.LogWarning("The bot's messages were not read: {Code} {Message}", exception.Code, exception.Message);
                    await Task.Delay(DelayAfter(exception), _clock, stop);
                    continue;
                }

                if (messages.Count > 0) await HandleAsync(messages, polling, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Cancelled, unlinked or the app is stopping
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reading the bot's messages stopped");
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (_polling == polling) StopPolling();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>The code still waiting and where to read from; on expiry the link goes back and the reading ends.</summary>
    private async Task<(PendingLink? Pending, long? After)> NextWaitAsync(CancellationTokenSource polling, CancellationToken stop)
    {
        AccountLink? expired = null;
        await _gate.WaitAsync(stop);
        try
        {
            if (_polling != polling) return (null, null);

            var current = await LoadAsync(stop);
            var now = _clock.GetUtcNow();
            var next = current.Expire(now);
            if (next != current)
            {
                _state = next;
                expired = next.ViewAt(now);
            }

            if (next.Pending is { } pending) return (pending, _lastMessage);
            StopPolling();
        }
        finally
        {
            _gate.Release();
        }

        if (expired is not null) await _events.AccountChangedAsync(expired, CancellationToken.None);
        return (null, null);
    }

    private async Task HandleAsync(IReadOnlyList<ChatMessage> messages, CancellationTokenSource polling, CancellationToken stop)
    {
        var replies = new List<(long ChatId, string Text)>();
        AccountLink? linked = null;
        await _gate.WaitAsync(stop);
        try
        {
            if (_polling != polling) return;

            var current = await LoadAsync(stop);
            foreach (var message in messages)
            {
                _lastMessage = message.Sequence;
                if (CodeOf(message.Text) is not { } code) continue;

                var now = _clock.GetUtcNow();
                var user = new LinkedUser(message.From.Id, message.From.FirstName, message.From.Username);
                if (linked is null && current.Accept(code, user, now) is { } accepted)
                {
                    current = accepted;
                    linked = accepted.ViewAt(now);
                    _logger.LogInformation("The account {UserId} is linked", user.Id);
                    replies.Add((message.ChatId, Greeting));
                }
                else
                {
                    replies.Add((message.ChatId, code.Length == 0 ? Hint : Outdated));
                }
            }

            _state = current;
            // The account and how far the messages are read, so a restart does not read them again
            await SaveAsync(stop);
            if (linked is not null) StopPolling();
        }
        finally
        {
            _gate.Release();
        }

        if (linked is not null) await _events.AccountChangedAsync(linked, CancellationToken.None);
        foreach (var (chatId, text) in replies) await ReplyAsync(chatId, text);
    }

    private async Task ReplyAsync(long chatId, string text)
    {
        try
        {
            await _chat.SendAsync(chatId, text, CancellationToken.None);
        }
        catch (AppException exception)
        {
            // The link is made either way; a lost reply only loses the bot's words
            _logger.LogWarning("The bot did not answer in a chat: {Code} {Message}", exception.Code, exception.Message);
        }
    }

    /// <summary>Under the gate: the state, read from the repository the first time.</summary>
    private async Task<AccountState> LoadAsync(CancellationToken cancellationToken)
    {
        if (_state is not null) return _state;

        var record = await _repository.GetAsync(cancellationToken);
        _lastMessage = record.LastMessage;
        _state = record.User is { } user
            ? AccountState.Linked(user).Checked(record.BotBlocked ? AccountProblem.BotBlocked : null)
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
            new AccountRecord(_state?.User, _state?.Problem == AccountProblem.BotBlocked, _lastMessage),
            cancellationToken);

    /// <summary>Under the gate. Not disposed: the reading may still hold its token; it has no timer to free.</summary>
    private void StopPolling()
    {
        _polling?.Cancel();
        _polling = null;
    }

    /// <summary>
    /// The code Start brought: `/start &lt;code&gt;` from a link with one, an empty one from a
    /// plain Start, null for any other message.
    /// </summary>
    internal static string? CodeOf(string? text)
    {
        if (text is null || !text.StartsWith(StartCommand, StringComparison.Ordinal)) return null;

        var rest = text[StartCommand.Length..];
        // "/start@StickerSnapAppBot code" in a group, "/started" is not the command
        if (rest.StartsWith('@')) rest = rest[(rest.IndexOfAny([' ', '\n']) is var space and >= 0 ? space : rest.Length)..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0])) return null;
        return rest.Trim();
    }

    private static TimeSpan DelayAfter(AppException exception) =>
        exception.Code == ErrorCode.RateLimited && exception.Params.TryGetValue("retryAfterSec", out var seconds)
            ? TimeSpan.FromSeconds(Convert.ToDouble(seconds, System.Globalization.CultureInfo.InvariantCulture))
            : RetryDelay;

    /// <summary>A start parameter of the messenger: up to 64 of A–Z, a–z, 0–9, _ and -.</summary>
    private static string NewCode() =>
        RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 24);
}
