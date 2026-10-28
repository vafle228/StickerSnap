using StickerSnap.Application.Projects;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>
/// The trash of macOS, which Finder shows as «Корзина»: the user's ~/.Trash. The file keeps its
/// name there while it is free, the way Finder names a second one: `a.ssproj`, then `a 2.ssproj`.
/// </summary>
/// <param name="trashDir">~/.Trash</param>
internal sealed class MacTrash(string trashDir) : ITrash
{
    /// <summary>The trash of the user the server runs as.</summary>
    public static MacTrash ForUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash"));

    public Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return Task.CompletedTask;

        try
        {
            Directory.CreateDirectory(trashDir);
            var stem = Path.GetFileNameWithoutExtension(full);
            var extension = Path.GetExtension(full);
            for (var attempt = 1; ; attempt++)
            {
                var name = attempt == 1 ? Path.GetFileName(full) : $"{stem} {attempt}{extension}";
                var target = Path.Combine(trashDir, name);
                if (File.Exists(target) || Directory.Exists(target)) continue;
                try
                {
                    File.Move(full, target);
                    return Task.CompletedTask;
                }
                catch (IOException) when (File.Exists(target))
                {
                    // Taken a moment ago: try the next name
                }
            }
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            throw IoErrors.Of(exception, $"{full} to the trash");
        }
    }
}
