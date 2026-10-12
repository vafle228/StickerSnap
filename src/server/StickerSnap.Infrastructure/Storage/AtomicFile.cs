using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>
/// Writes a file of the data directory whole or not at all: the bytes go to a temporary file
/// beside it, reach the disk, and only then replace the old file. A crash or a full disk midway
/// leaves the previous version in place. Projects and packs keep their registries the same way.
/// </summary>
internal static class AtomicFile
{
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    public static Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        PutAsync(path, content, overwrite: true, cancellationToken);

    /// <summary>Like <see cref="WriteAsync"/>, for a file that must be new.</summary>
    /// <returns>False when a file is already at <paramref name="path"/>: it is left as it was</returns>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    public static async Task<bool> CreateAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (File.Exists(path)) return false;
        try
        {
            await PutAsync(path, content, overwrite: false, cancellationToken);
            return true;
        }
        catch (AppException exception) when (exception.InnerException is IOException && File.Exists(path))
        {
            // Another file took the name between the check and the move
            return false;
        }
    }

    private static async Task PutAsync(string path, ReadOnlyMemory<byte> content, bool overwrite, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(
            Path.GetDirectoryName(path) ?? ".",
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception) || exception is OperationCanceledException)
        {
            TryDelete(temporary);
            if (exception is OperationCanceledException) throw;
            throw IoErrors.Of(exception, path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            // Nothing more to do: a stray .tmp beside the file harms nothing
        }
    }
}
