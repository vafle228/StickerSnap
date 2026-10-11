namespace StickerSnap.Application.Settings;

/// <summary>The file system's side of choosing a folder.</summary>
public interface IFolderAccess
{
    /// <summary>Creates the folder if it is missing and makes sure files can be written in it.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed without access, disk_full without space</exception>
    Task PrepareAsync(string path, CancellationToken cancellationToken);
}
