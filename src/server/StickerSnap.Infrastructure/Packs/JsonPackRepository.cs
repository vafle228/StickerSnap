using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Packs;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Packs;

/// <summary>
/// packs.json in the data directory: the packs with their stickers' emoji and projects. Read once
/// and then kept in memory, written whole on every change. A file that cannot be read as the list
/// is moved aside as packs.json.bad and the list starts empty: the packs are safe in Telegram and
/// come back by «Добавить по ссылке», only where their stickers came from is lost.
/// </summary>
internal sealed class JsonPackRepository(IOptions<DataOptions> options, ILogger<JsonPackRepository> logger) : IPackRepository
{
    public const string FileName = "packs.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        WriteIndented = true,
        // Titles and emoji as they are, so the file reads by eye
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<Pack>? _packs;

    private string Path => System.IO.Path.Combine(options.Value.DataDir, FileName);

    public async Task<IReadOnlyList<Pack>> ListAsync(CancellationToken cancellationToken) =>
        await UnderGateAsync(packs => Task.FromResult<IReadOnlyList<Pack>>([.. packs]), cancellationToken);

    public async Task<Pack?> FindAsync(string name, CancellationToken cancellationToken) =>
        await UnderGateAsync(packs => Task.FromResult(packs.Find(pack => Same(pack.Name, name))), cancellationToken);

    public Task SaveAsync(Pack pack, CancellationToken cancellationToken) =>
        UnderGateAsync(async packs =>
        {
            var changed = new List<Pack>(packs);
            var at = changed.FindIndex(known => Same(known.Name, pack.Name));
            if (at >= 0) changed[at] = pack;
            else changed.Add(pack);
            await WriteAsync(changed, cancellationToken);
            return true;
        }, cancellationToken);

    public Task RemoveAsync(string name, CancellationToken cancellationToken) =>
        UnderGateAsync(async packs =>
        {
            if (!packs.Exists(pack => Same(pack.Name, name))) return true;
            await WriteAsync([.. packs.Where(pack => !Same(pack.Name, name))], cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>Telegram tells pack names apart without case.</summary>
    private static bool Same(string name, string other) => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);

    private async Task<T> UnderGateAsync<T>(Func<List<Pack>, Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _packs ??= await LoadAsync(cancellationToken);
            return await action(_packs);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The list in memory changes only once the file has it, so a failed write changes nothing.</summary>
    private async Task WriteAsync(List<Pack> packs, CancellationToken cancellationToken)
    {
        var stored = new Stored(Stored.CurrentVersion, [.. packs.Select(StoredPack.From)]);
        await AtomicFile.WriteAsync(Path, JsonSerializer.SerializeToUtf8Bytes(stored, Json), cancellationToken);
        _packs = packs;
    }

    private async Task<List<Pack>> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path;
        if (!File.Exists(path)) return [];

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<Stored>(stream, Json, cancellationToken);
            if (stored is { Version: Stored.CurrentVersion })
                return [.. stored.Packs.Select(pack => pack.ToPack()).DistinctBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase)];
            logger.LogWarning("{Path} has version {Version}, this server knows {Known}", path, stored?.Version, Stored.CurrentVersion);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{Path} is not valid JSON of the pack list", path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            // Unreadable, not broken: leave the file be. The next change writes over it
            logger.LogWarning(exception, "{Path} cannot be read, the pack list starts empty", path);
            return [];
        }

        SetAside(path);
        return [];
    }

    /// <summary>Keeps the broken file for whoever wants to look at it, and out of the way of the next write.</summary>
    private void SetAside(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
            logger.LogWarning("A broken pack list moved to {Path}.bad, the list starts empty", path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            logger.LogWarning(exception, "A broken pack list in {Path} could not be moved aside", path);
        }
    }

    // The file's shape, apart from the domain's: it outlives renames in the code.

    private sealed record Stored(int Version, IReadOnlyList<StoredPack> Packs)
    {
        public const int CurrentVersion = 1;
    }

    private sealed record StoredPack(
        string Name,
        string Title,
        PackKind Kind,
        PresenceStatus Status,
        DateTimeOffset? CheckedAt,
        IReadOnlyList<StoredSticker> Stickers)
    {
        public static StoredPack From(Pack pack) => new(
            pack.Name, pack.Title, pack.Kind, pack.Status, pack.CheckedAt, [.. pack.Stickers.Select(StoredSticker.From)]);

        public Pack ToPack() => new(Name, Title, Kind, Status, CheckedAt, [.. Stickers.Select(sticker => sticker.ToSticker())]);
    }

    /// <param name="FileUniqueId">Telegram's names, so the file says what the ids are</param>
    /// <param name="Format">Absent in a file from before pack copies: a video</param>
    private sealed record StoredSticker(
        string FileUniqueId,
        string FileId,
        IReadOnlyList<string> Emoji,
        StoredOrigin? Origin,
        StickerFormat Format = StickerFormat.Video)
    {
        public static StoredSticker From(PackSticker sticker) => new(
            sticker.Id,
            sticker.FileId,
            sticker.Emoji,
            sticker.Origin is { } origin ? new StoredOrigin(origin.ProjectId, origin.ProjectName, origin.SegmentId) : null,
            sticker.Format);

        public PackSticker ToSticker() => new(
            FileUniqueId,
            FileId,
            Emoji,
            Origin is { } origin ? new StickerOrigin(origin.ProjectId, origin.ProjectName, origin.SegmentId) : null,
            Format);
    }

    private sealed record StoredOrigin(Guid ProjectId, string ProjectName, string SegmentId);
}
