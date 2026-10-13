using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Files;

/// <summary>GET /api/v1/files/{fileId} over <see cref="IRenderedFiles"/>.</summary>
public sealed class FileService(IRenderedFiles files) : IFileService
{
    /// <inheritdoc />
    public async Task<string> PathOfAsync(string fileId, CancellationToken cancellationToken) =>
        await files.FindAsync(fileId, cancellationToken)
        ?? throw new AppException(ErrorCode.FileNotFound, $"There is no file {fileId}, or it was removed");
}
