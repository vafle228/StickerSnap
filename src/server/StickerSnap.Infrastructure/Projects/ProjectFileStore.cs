using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Projects;

/// <summary>Projects as .ssproj files on the local disk, written whole or not at all.</summary>
internal sealed class ProjectFileStore : IProjectStore
{
    /// <summary>A project is a few kilobytes; a file this big is a video given the wrong name.</summary>
    internal const int MaxBytes = 16 * 1024 * 1024;

    public async Task<Project> ReadAsync(string path, CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            // Sharing delete lets a save of the same project replace the file while it is being read
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous);
            if (stream.Length > MaxBytes)
                throw new AppException(ErrorCode.ProjectInvalid, $"{path} is not a project: {stream.Length} bytes")
                {
                    Log = $"$: {stream.Length} bytes, a project is at most {MaxBytes}",
                };

            content = new byte[stream.Length];
            await stream.ReadExactlyAsync(content, cancellationToken);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            throw new AppException(ErrorCode.ProjectNotFound, $"Cannot read {path}: {exception.Message}", exception)
            {
                Log = exception.Message,
            };
        }

        return ProjectFile.Current.Read(content, path);
    }

    public Task WriteAsync(string path, Project project, CancellationToken cancellationToken) =>
        AtomicFile.WriteAsync(path, ProjectFile.Current.Write(project), cancellationToken);
}
