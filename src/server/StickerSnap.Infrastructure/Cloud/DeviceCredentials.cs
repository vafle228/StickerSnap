using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Cloud;

/// <summary>
/// device.json in the data directory: the id of this installation, made on first use, and the
/// key the cloud service gave this computer when it was linked. The key stays here: it is never
/// logged, never shown and never sent to the SPA. A file that cannot be read means a fresh
/// installation that is not linked: the user links again, which is better than an app that does
/// not start.
/// </summary>
internal sealed class DeviceCredentials(IOptions<DataOptions> options, ILogger<DeviceCredentials> logger) : IDisposable
{
    public const string FileName = "device.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Stored? _stored;

    private string Path => System.IO.Path.Combine(options.Value.DataDir, FileName);

    public void Dispose() => _gate.Dispose();

    /// <summary>The same id from the first launch on, so the service knows the computer when it is linked again.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full when a new id cannot be kept</exception>
    public async Task<Guid> GetInstallationIdAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadAsync(cancellationToken);
            if (stored.InstallationId is { } id && id != Guid.Empty) return id;

            var made = stored with { InstallationId = Guid.NewGuid() };
            await WriteAsync(made, cancellationToken);
            return made.InstallationId!.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The key of this computer; null while it is not linked.</summary>
    public async Task<string?> GetKeyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadAsync(cancellationToken)).DeviceKey;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    public Task SaveKeyAsync(string key, CancellationToken cancellationToken) =>
        ChangeAsync(stored => stored with { DeviceKey = key }, cancellationToken);

    /// <summary>
    /// Drops the key: after an unlink, or when the service no longer takes it. With
    /// <paramref name="only"/> set, a key saved since by a new link is kept.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    public Task ForgetKeyAsync(CancellationToken cancellationToken, string? only = null) =>
        ChangeAsync(
            stored => only is null || stored.DeviceKey == only ? stored with { DeviceKey = null } : stored,
            cancellationToken);

    private async Task ChangeAsync(Func<Stored, Stored> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadAsync(cancellationToken);
            var changed = change(stored);
            if (changed != stored) await WriteAsync(changed, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Stored> ReadAsync(CancellationToken cancellationToken)
    {
        if (_stored is { } known) return known;

        var path = Path;
        _stored = Stored.Empty;
        if (!File.Exists(path)) return _stored;

        try
        {
            await using var stream = File.OpenRead(path);
            _stored = await JsonSerializer.DeserializeAsync<Stored>(stream, Json, cancellationToken) ?? Stored.Empty;
        }
        catch (JsonException)
        {
            // Not the exception itself: its message may quote the file, and so the key
            logger.LogWarning("{Path} is not valid JSON, this computer starts as a new installation that is not linked", path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            logger.LogWarning(exception, "{Path} cannot be read, this computer starts as a new installation that is not linked", path);
        }
        return _stored;
    }

    private async Task WriteAsync(Stored stored, CancellationToken cancellationToken)
    {
        await AtomicFile.WriteAsync(Path, JsonSerializer.SerializeToUtf8Bytes(stored, Json), cancellationToken);
        _stored = stored;
    }

    /// <summary>The file's shape, apart from the code's: it outlives renames.</summary>
    private sealed record Stored(int Version, Guid? InstallationId, string? DeviceKey)
    {
        public static readonly Stored Empty = new(1, null, null);
    }
}
