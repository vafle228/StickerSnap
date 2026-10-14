using StickerSnap.Application.Events;
using StickerSnap.Application.Packs;
using StickerSnap.Application.ProjectMedia;
using StickerSnap.Core.Packs;

namespace StickerSnap.Tests.Application.Packs;

/// <summary>The pack registry in memory, for the use cases' tests.</summary>
internal sealed class InMemoryPackRepository : IPackRepository
{
    private readonly List<Pack> _packs = [];

    public IReadOnlyList<Pack> Packs => _packs;

    public Task<IReadOnlyList<Pack>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Pack>>([.. _packs]);

    public Task<Pack?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_packs.Find(pack => string.Equals(pack.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task SaveAsync(Pack pack, CancellationToken cancellationToken)
    {
        var at = _packs.FindIndex(known => string.Equals(known.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) _packs[at] = pack;
        else _packs.Add(pack);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string name, CancellationToken cancellationToken)
    {
        _packs.RemoveAll(pack => string.Equals(pack.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
}

/// <summary>A media cache that makes each file once, in a temporary directory.</summary>
internal sealed class TempMediaCache : IMediaCache, IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("stickersnap-cache-");

    public int Made { get; private set; }

    public string? Find(string key, string extension)
    {
        var path = PathOf(key, extension);
        return File.Exists(path) ? path : null;
    }

    public async Task<string> GetOrAddAsync(
        string key,
        string extension,
        Func<string, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (Find(key, extension) is { } kept) return kept;
        var path = PathOf(key, extension);
        await write(path, cancellationToken);
        Made++;
        return path;
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private string PathOf(string key, string extension) => Path.Combine(_dir.FullName, $"{key}.{extension}");
}

/// <summary>The packChanged events a use case sent.</summary>
internal static class PackEvents
{
    public static IReadOnlyList<Pack> Sent(IEventPublisher events) =>
        [.. events.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEventPublisher.PackChangedAsync))
            .Select(call => (Pack)call.GetArguments()[0]!)];
}
