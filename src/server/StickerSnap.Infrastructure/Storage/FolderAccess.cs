using StickerSnap.Application.Settings;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>Creates a chosen folder and proves it takes files by writing and removing a probe.</summary>
internal sealed class FolderAccess : IFolderAccess
{
    public async Task PrepareAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".stickersnap-probe-{Guid.NewGuid():N}");
            await using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            throw IoErrors.Of(exception, $"the folder {path}");
        }
    }
}
