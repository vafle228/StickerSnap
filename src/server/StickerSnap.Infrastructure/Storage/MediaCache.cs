using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.ProjectMedia;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>
/// The media cache in the data directory: media/&lt;key&gt;.&lt;ext&gt;, written beside under a hidden
/// part name and moved in whole, so a file there is always complete. A hit refreshes the file's
/// write time; files untouched for <see cref="Unused"/> go on the next write, at most once an hour.
/// </summary>
internal sealed class MediaCache(IOptions<DataOptions> options, TimeProvider clock, ILogger<MediaCache> logger) : IMediaCache
{
    internal static readonly TimeSpan Unused = TimeSpan.FromDays(30);

    private static readonly TimeSpan SweepEvery = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _making = new(StringComparer.Ordinal);
    private DateTimeOffset _swept = DateTimeOffset.MinValue;

    internal string Dir => Path.Combine(options.Value.DataDir, "media");

    public string? Find(string key, string extension)
    {
        var path = PathOf(key, extension);
        if (!File.Exists(path)) return null;
        Touch(path);
        return path;
    }

    public async Task<string> GetOrAddAsync(
        string key,
        string extension,
        Func<string, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (Find(key, extension) is { } kept) return kept;

        var name = $"{key}.{extension}";
        // Made apart from the caller: a timeline scrolled past drops its request, the next one finds the file
        var making = _making.GetOrAdd(name, _ => new Lazy<Task<string>>(() => Task.Run(() => MakeAsync(key, extension, write))));
        try
        {
            return await making.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            if (making.Value.IsCompleted) _making.TryRemove(KeyValuePair.Create(name, making));
        }
    }

    private async Task<string> MakeAsync(string key, string extension, Func<string, CancellationToken, Task> write)
    {
        Sweep();
        var path = PathOf(key, extension);
        var part = Path.Combine(Dir, $".{key}.{Guid.NewGuid():N}.part.{extension}");
        try
        {
            Directory.CreateDirectory(Dir);
        }
        catch (Exception e) when (IoErrors.IsIoError(e))
        {
            throw IoErrors.Of(e, Dir);
        }

        try
        {
            await write(part, CancellationToken.None);
            File.Move(part, path, overwrite: true);
            return path;
        }
        catch (Exception e) when (IoErrors.IsIoError(e))
        {
            throw IoErrors.Of(e, path);
        }
        finally
        {
            DeleteQuietly(part);
        }
    }

    private string PathOf(string key, string extension)
    {
        // Only what MediaKeys makes: no key reaches outside the cache's folder
        if (key.Length == 0 || !key.All(char.IsAsciiHexDigitLower) || !extension.All(char.IsAsciiLetterLower))
            throw new ArgumentException($"Not a media cache key: {key}.{extension}", nameof(key));
        return Path.Combine(Dir, $"{key}.{extension}");
    }

    private void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, clock.GetUtcNow().UtcDateTime);
        }
        catch (Exception e) when (IoErrors.IsIoError(e))
        {
            // A file being read stays; it is only removed a little sooner
        }
    }

    /// <summary>Files nobody asked for in <see cref="Unused"/>, and parts a stopped server left behind.</summary>
    private void Sweep()
    {
        var now = clock.GetUtcNow();
        if (now - _swept < SweepEvery || !Directory.Exists(Dir)) return;
        _swept = now;

        foreach (var file in new DirectoryInfo(Dir).EnumerateFiles())
        {
            var age = now.UtcDateTime - file.LastWriteTimeUtc;
            var leftover = file.Name.StartsWith('.') && age > TimeSpan.FromDays(1);
            if (age <= Unused && !leftover) continue;
            try
            {
                file.Delete();
            }
            catch (Exception e) when (IoErrors.IsIoError(e))
            {
                logger.LogDebug(e, "Media cache file {File} stays until the next sweep", file.FullName);
            }
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (IoErrors.IsIoError(e))
        {
            // Left for the sweep
        }
    }
}
