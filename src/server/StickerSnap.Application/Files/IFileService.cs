namespace StickerSnap.Application.Files;

/// <summary>GET /api/v1/files/{fileId}: a draft of the exact preview or an exported sticker.</summary>
public interface IFileService
{
    /// <summary>Where the file is, for the answer to read it with Range.</summary>
    /// <exception cref="Core.Errors.AppException">file_not_found for an id it does not know, or a file removed since</exception>
    Task<string> PathOfAsync(string fileId, CancellationToken cancellationToken);
}
