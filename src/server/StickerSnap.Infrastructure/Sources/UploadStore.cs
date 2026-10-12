using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Sources;

/// <summary>
/// Uploads in the data directory, one folder per upload holding its one file:
/// uploads/&lt;id&gt;/cat.gif. A project takes its upload by moving the folder to sources/&lt;id&gt;,
/// which nothing clears. The folder's write time is the upload's age.
/// </summary>
internal sealed class UploadStore(
    IOptions<DataOptions> data,
    IOptions<UploadOptions> options,
    TimeProvider clock,
    ILogger<UploadStore> logger) : IUploadStore
{
    /// <summary>How long an upload nobody takes is kept.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    private const int BufferBytes = 81920;

    internal string UploadsDir => Path.Combine(data.Value.DataDir, "uploads");

    internal string SourcesDir => Path.Combine(data.Value.DataDir, "sources");

    public async Task<string> SaveAsync(string fileName, ReadOnlyMemory<byte> head, Stream content, CancellationToken cancellationToken)
    {
        ClearExpired();

        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(UploadsDir, id);
        var path = Path.Combine(folder, fileName);
        try
        {
            Directory.CreateDirectory(folder);
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BufferBytes, FileOptions.Asynchronous))
            {
                await file.WriteAsync(head, cancellationToken);
                await CopyAsync(content, file, head.Length, cancellationToken);
            }
            return id;
        }
        catch (Exception exception)
        {
            // A dropped request, a file over the limit, a full disk: nothing of it stays
            TryDelete(folder);
            if (!IoErrors.IsIoError(exception)) throw;
            throw IoErrors.Of(exception, path);
        }
    }

    public string? Find(string uploadId)
    {
        var folder = FolderOf(UploadsDir, uploadId);
        if (folder is null || !Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder).FirstOrDefault();
    }

    public Task<string> KeepAsync(string uploadId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = Find(uploadId) ?? throw new AppException(ErrorCode.SourceNotFound, $"No upload {uploadId} on this server");
        var kept = FolderOf(SourcesDir, uploadId)!;
        try
        {
            Directory.CreateDirectory(SourcesDir);
            Directory.Move(Path.GetDirectoryName(file)!, kept);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            throw IoErrors.Of(exception, kept);
        }
        return Task.FromResult(Path.Combine(kept, Path.GetFileName(file)));
    }

    /// <summary>The folder of an upload; null for an id that is not a GUID, so no id reaches outside the store.</summary>
    private static string? FolderOf(string root, string uploadId) =>
        Guid.TryParse(uploadId, out var id) ? Path.Combine(root, id.ToString("N")) : null;

    private async Task CopyAsync(Stream content, FileStream file, long written, CancellationToken cancellationToken)
    {
        var limit = options.Value.MaxBytes;
        var buffer = new byte[BufferBytes];
        while (await ReadAsync(content, buffer, cancellationToken) is var read and > 0)
        {
            written += read;
            if (written > limit)
                throw new AppException(ErrorCode.BadRequest, $"The upload is over {limit} bytes")
                {
                    Fields = new Dictionary<string, string[]> { ["file"] = ["too_large"] },
                };
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    /// <summary>A body that breaks off is a request dropped, not a failure to save: nothing is answered to it.</summary>
    private static async Task<int> ReadAsync(Stream content, byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await content.ReadAsync(buffer, cancellationToken);
        }
        catch (IOException exception)
        {
            throw new OperationCanceledException("The request was dropped before the whole file came", exception);
        }
    }

    /// <summary>Uploads older than <see cref="Lifetime"/>; one that cannot be removed now goes next time.</summary>
    private void ClearExpired()
    {
        if (!Directory.Exists(UploadsDir)) return;
        var before = clock.GetUtcNow().UtcDateTime - Lifetime;
        foreach (var folder in Directory.EnumerateDirectories(UploadsDir))
        {
            if (Directory.GetLastWriteTimeUtc(folder) >= before) continue;
            if (TryDelete(folder)) logger.LogInformation("Removed the upload {Folder}, which no project took", folder);
        }
    }

    private static bool TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            return false;
        }
    }
}
