namespace StickerSnap.Application.Projects;

/// <summary>The system's trash, where «Удалить с диска» sends a file so the user can still bring it back.</summary>
public interface ITrash
{
    /// <summary>Moves the file to the trash; a file that is already gone is nothing to do.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed when the file cannot be moved there</exception>
    Task MoveToTrashAsync(string path, CancellationToken cancellationToken);
}
