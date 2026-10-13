using StickerSnap.Application.Exports;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Files;

/// <summary>
/// The stickers folder on disk. A sticker is rendered to a hidden `.&lt;id&gt;.part.webm` beside
/// where it goes, so taking its name is a rename on the same disk, and a name another file has is
/// never written over.
/// </summary>
internal sealed class StickerFolder : IStickerFolder
{
    public Task<StickerSlot> ReserveAsync(string folder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            throw IoErrors.Of(exception, folder);
        }
        return Task.FromResult(new StickerSlot(folder, Path.Combine(folder, $".{Guid.NewGuid():N}.part.webm")));
    }

    public Task<string> KeepAsync(StickerSlot slot, IEnumerable<string> names, string? replacing, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var name in names)
        {
            var path = Path.Combine(slot.Folder, name);
            var own = replacing is not null && SamePath(path, replacing);
            if (!own && File.Exists(path)) continue;
            try
            {
                File.Move(slot.TemporaryPath, path, overwrite: own);
                return Task.FromResult(path);
            }
            catch (IOException) when (!own && File.Exists(path))
            {
                // Another file took the name between the check and the move: the next name
            }
            catch (Exception exception) when (IoErrors.IsIoError(exception))
            {
                throw IoErrors.Of(exception, path);
            }
        }

        throw new InvalidOperationException("The names of a sticker never run out");
    }

    public void Discard(StickerSlot slot)
    {
        try
        {
            File.Delete(slot.TemporaryPath);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            // A stray .part.webm harms nothing
        }
    }

    private static bool SamePath(string a, string b) => string.Equals(
        Path.GetFullPath(a),
        Path.GetFullPath(b),
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
