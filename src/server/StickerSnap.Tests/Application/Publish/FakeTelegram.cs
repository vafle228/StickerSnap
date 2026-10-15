using StickerSnap.Application.Packs;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Packs;

namespace StickerSnap.Tests.Application.Publish;

/// <summary>
/// The packs of the bot in Telegram, in memory, with a script: a call of a method may fail the
/// way the Bot API fails (429, 400, no network) or stop until the test lets it go on. Every call
/// is written down in <see cref="Calls"/>, as «upload», «create cats», «add cats», «replace cats F2».
/// </summary>
internal sealed class FakeTelegram : IStickerPublisher
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string Title, PackKind Kind, List<PublishedSticker> Stickers)> _packs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<Func<Task>>> _script = [];
    private readonly List<string> _calls = [];
    private int _next;

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate) return [.. _calls];
        }
    }

    /// <summary>A pack Telegram has already, with stickers U1.., F1...</summary>
    public void Has(string name, PackKind kind, int stickers)
    {
        lock (_gate)
            _packs[name] = ("Коты", kind, [.. Enumerable.Range(0, stickers).Select(_ => NewSticker("🐱"))]);
    }

    public PublishedPack? Pack(string name)
    {
        lock (_gate) return Snapshot(name);
    }

    /// <summary>The next calls of <paramref name="method"/> («upload», «create», «add», «replace», «get») go as scripted, in order.</summary>
    public void Then(string method, Func<Task> step)
    {
        lock (_gate)
        {
            if (!_script.TryGetValue(method, out var steps)) _script[method] = steps = new Queue<Func<Task>>();
            steps.Enqueue(step);
        }
    }

    /// <summary>The next call of <paramref name="method"/> succeeds as Telegram would.</summary>
    public void ThenSucceeds(string method) => Then(method, () => Task.CompletedTask);

    public void ThenFails(string method, AppException exception) => Then(method, () => Task.FromException(exception));

    /// <summary>429: «Too Many Requests: retry after <paramref name="seconds"/>».</summary>
    public static AppException RateLimited(int seconds) =>
        new(ErrorCode.RateLimited, "Bot API refused: 429") { Params = new Dictionary<string, object> { ["retryAfterSec"] = seconds } };

    public static AppException Refused(string description) =>
        new(ErrorCode.TelegramError, $"Bot API refused: 400 {description}") { Log = description };

    public Task<PublishedPack?> GetPackAsync(string name, CancellationToken cancellationToken) =>
        RunAsync("get", name, cancellationToken, () => Snapshot(name));

    public Task<StickerFile.Uploaded> UploadAsync(long ownerId, string path, CancellationToken cancellationToken) =>
        RunAsync("upload", null, cancellationToken, () => new StickerFile.Uploaded($"up-{Path.GetFileName(path)}"));

    public Task CreatePackAsync(
        long ownerId, string name, string title, PackKind kind, IReadOnlyList<NewSticker> stickers, CancellationToken cancellationToken) =>
        RunAsync("create", name, cancellationToken, () =>
        {
            if (_packs.ContainsKey(name))
                throw new AppException(ErrorCode.PackNameTaken, $"{name} is taken");
            _packs[name] = (title, kind, [.. stickers.Select(sticker => NewSticker(sticker.Emoji[0]))]);
            return true;
        });

    public Task AddStickerAsync(long ownerId, string pack, NewSticker sticker, CancellationToken cancellationToken) =>
        RunAsync("add", pack, cancellationToken, () =>
        {
            Find(pack).Stickers.Add(NewSticker(sticker.Emoji[0]));
            return true;
        });

    public Task ReplaceStickerAsync(long ownerId, string pack, string oldStickerId, NewSticker sticker, CancellationToken cancellationToken) =>
        RunAsync("replace", $"{pack} {oldStickerId}", cancellationToken, () =>
        {
            var stickers = Find(pack).Stickers;
            var at = stickers.FindIndex(old => old.Id == oldStickerId);
            if (at < 0) throw Refused("STICKER_NOT_FOUND");
            stickers[at] = NewSticker(sticker.Emoji[0]);
            return true;
        });

    public Task DeleteStickerAsync(string stickerId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task DownloadAsync(string stickerId, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>A call canceled while it was held does not reach the pack, as an HTTP request would not.</summary>
    private async Task<T> RunAsync<T>(string method, string? what, CancellationToken cancellationToken, Func<T> act)
    {
        Func<Task>? step;
        lock (_gate)
        {
            _calls.Add(what is null ? method : $"{method} {what}");
            step = _script.TryGetValue(method, out var steps) && steps.Count > 0 ? steps.Dequeue() : null;
        }
        if (step is not null) await step();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return act();
    }

    private (string Title, PackKind Kind, List<PublishedSticker> Stickers) Find(string name) =>
        _packs.TryGetValue(name, out var pack) ? pack : throw new AppException(ErrorCode.PackNotFound, $"No pack {name}");

    private PublishedPack? Snapshot(string name) =>
        _packs.TryGetValue(name, out var pack) ? new PublishedPack(name, pack.Title, pack.Kind, [.. pack.Stickers]) : null;

    private PublishedSticker NewSticker(string emoji)
    {
        var n = ++_next;
        return new PublishedSticker($"F{n}", $"U{n}", emoji);
    }
}
