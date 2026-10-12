namespace StickerSnap.Application.Projects;

/// <summary>
/// Waits for a missing source to come back, so an open project's «Исходник не найден» clears by
/// itself when the user puts the video back where it was.
/// </summary>
public interface ISourceWatch
{
    /// <summary>
    /// Calls <paramref name="appeared"/> once, when a file appears at <paramref name="path"/>. A new
    /// watch of the same project replaces the old one. A path whose folder is gone too is not watched.
    /// </summary>
    void Watch(Guid projectId, string path, Func<Task> appeared);

    /// <summary>Stops waiting for the project's source; nothing happens for a project not watched.</summary>
    void Forget(Guid projectId);
}
