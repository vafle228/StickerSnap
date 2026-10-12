using System.Globalization;
using System.Text;
using StickerSnap.Application.Projects;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>
/// The home trash of the FreeDesktop.org Trash specification, which GNOME, KDE and the others
/// show as «Корзина»: the file goes to Trash/files, and Trash/info gets a .trashinfo that says where
/// it came from and when, so «Восстановить» can put it back.
/// </summary>
/// <param name="trashDir">$XDG_DATA_HOME/Trash, by default ~/.local/share/Trash</param>
internal sealed class FreedesktopTrash(string trashDir, TimeProvider clock) : ITrash
{
    /// <summary>The trash of the user the server runs as.</summary>
    public static FreedesktopTrash ForUser(TimeProvider clock)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome) || !Path.IsPathRooted(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return new FreedesktopTrash(Path.Combine(dataHome, "Trash"), clock);
    }

    public async Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return;

        var files = Path.Combine(trashDir, "files");
        var info = Path.Combine(trashDir, "info");
        string? infoFile = null;
        try
        {
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);

            // The .trashinfo is created first and exclusively: it claims the name (specification, «Contents of a trash directory»)
            var (name, stream) = Claim(info, Path.GetFileName(full), files);
            infoFile = Path.Combine(info, name + ".trashinfo");
            await using (stream)
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(InfoOf(full)), cancellationToken);
            }

            File.Move(full, Path.Combine(files, name));
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            if (infoFile is not null) TryDelete(infoFile);
            throw IoErrors.Of(exception, $"{full} to the trash");
        }
    }

    /// <summary>The first free name: `a.ssproj`, then `a.2.ssproj`, `a.3.ssproj`.</summary>
    private static (string Name, FileStream Stream) Claim(string info, string fileName, string files)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var attempt = 1; ; attempt++)
        {
            var name = attempt == 1 ? fileName : $"{stem}.{attempt}{extension}";
            if (File.Exists(Path.Combine(files, name)) || Directory.Exists(Path.Combine(files, name))) continue;
            try
            {
                return (name, new FileStream(Path.Combine(info, name + ".trashinfo"), FileMode.CreateNew, FileAccess.Write));
            }
            catch (IOException) when (File.Exists(Path.Combine(info, name + ".trashinfo")))
            {
                // Taken a moment ago: try the next name
            }
        }
    }

    private string InfoOf(string path)
    {
        var deleted = clock.GetLocalNow().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return $"[Trash Info]\nPath={encoded}\nDeletionDate={deleted}\n";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            // A stray .trashinfo without its file is skipped by every trash viewer
        }
    }
}
