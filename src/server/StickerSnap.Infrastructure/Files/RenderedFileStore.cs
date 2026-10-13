using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Files;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Files;

/// <summary>
/// The rendered files in the data directory: drafts as drafts/&lt;id&gt;.webm, their file's write
/// time their age, and files.json, the table of exported stickers by id. The table is read once
/// and kept in memory, written whole on every change; a table that cannot be read starts empty,
/// and an export of the project gives its file a new id.
/// </summary>
internal sealed class RenderedFileStore(IOptions<DataOptions> options, TimeProvider clock, ILogger<RenderedFileStore> logger)
    : IRenderedFiles
{
    public const string TableName = "files.json";

    /// <summary>How long a draft is served: the SPA fetches it as soon as its job is done.</summary>
    internal static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(1);

    private const string DraftExtension = ".webm";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, string>? _exports;

    internal string DraftsDir => Path.Combine(options.Value.DataDir, "drafts");

    private string TablePath => Path.Combine(options.Value.DataDir, TableName);

    public RenderedFile NewDraft()
    {
        ClearOldDrafts();
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(DraftsDir);
        return new RenderedFile(id, Path.Combine(DraftsDir, id + DraftExtension));
    }

    public Task<string> AddAsync(string path, CancellationToken cancellationToken) =>
        UnderGateAsync(async exports =>
        {
            var id = Guid.NewGuid().ToString("N");
            await WriteAsync(new Dictionary<string, string>(exports) { [id] = Path.GetFullPath(path) }, cancellationToken);
            return id;
        }, cancellationToken);

    public Task RemoveAsync(string fileId, CancellationToken cancellationToken) =>
        UnderGateAsync(async exports =>
        {
            if (!exports.ContainsKey(fileId)) return true;
            var rest = new Dictionary<string, string>(exports);
            rest.Remove(fileId);
            await WriteAsync(rest, cancellationToken);
            return true;
        }, cancellationToken);

    public async Task<string?> FindAsync(string fileId, CancellationToken cancellationToken)
    {
        // Only a GUID: no id reaches outside the drafts folder
        if (!Guid.TryParseExact(fileId, "N", out _)) return null;

        var draft = Path.Combine(DraftsDir, fileId + DraftExtension);
        if (File.Exists(draft)) return draft;

        var path = await UnderGateAsync(exports => Task.FromResult(exports.GetValueOrDefault(fileId)), cancellationToken);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>Drafts past their lifetime; one still open by an answer stays until next time.</summary>
    private void ClearOldDrafts()
    {
        if (!Directory.Exists(DraftsDir)) return;
        var now = clock.GetUtcNow();
        foreach (var draft in Directory.EnumerateFiles(DraftsDir, "*" + DraftExtension))
        {
            try
            {
                if (now - File.GetLastWriteTimeUtc(draft) >= DraftLifetime) File.Delete(draft);
            }
            catch (Exception exception) when (IoErrors.IsIoError(exception))
            {
                logger.LogDebug(exception, "Draft {Path} was not cleared", draft);
            }
        }
    }

    private async Task<T> UnderGateAsync<T>(Func<Dictionary<string, string>, Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _exports ??= await LoadAsync(cancellationToken);
            return await action(_exports);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The table in memory changes only once the file has it, so a failed write changes nothing.</summary>
    private async Task WriteAsync(Dictionary<string, string> exports, CancellationToken cancellationToken)
    {
        var stored = new Stored(Stored.CurrentVersion, exports.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value));
        await AtomicFile.WriteAsync(TablePath, JsonSerializer.SerializeToUtf8Bytes(stored, Json), cancellationToken);
        _exports = exports;
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        var path = TablePath;
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<Stored>(stream, Json, cancellationToken);
            if (stored is { Version: Stored.CurrentVersion })
                return new Dictionary<string, string>(stored.Exports, StringComparer.Ordinal);
            logger.LogWarning("{Path} has version {Version}, this server knows {Known}", path, stored?.Version, Stored.CurrentVersion);
        }
        catch (Exception exception) when (exception is JsonException || IoErrors.IsIoError(exception))
        {
            logger.LogWarning(exception, "{Path} cannot be read, exported files are served again once exported again", path);
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // The file's shape: the exported stickers' paths by their id
    private sealed record Stored(int Version, IReadOnlyDictionary<string, string> Exports)
    {
        public const int CurrentVersion = 1;
    }
}
