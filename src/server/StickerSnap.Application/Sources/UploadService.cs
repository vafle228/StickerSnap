using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;

namespace StickerSnap.Application.Sources;

/// <summary>Takes a browser's file in: tells its format by the first bytes, then streams it to the store.</summary>
public sealed class UploadService(IUploadStore store) : IUploadService
{
    /// <inheritdoc />
    public async Task<string> UploadAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        var head = new byte[MediaFormats.SniffBytes];
        var read = await content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        var name = SafeFileNames.Of(fileName);

        var format = MediaFormats.Sniff(head.AsSpan(0, read))
            ?? throw new AppException(ErrorCode.FormatUnsupported, $"{name} is not a video the editor opens")
            {
                Params = new Dictionary<string, object> { ["ext"] = Path.GetExtension(name).TrimStart('.').ToLowerInvariant() },
            };

        var stem = Path.GetFileNameWithoutExtension(name);
        var stored = $"{(stem.Length > 0 ? stem : SafeFileNames.Fallback)}.{MediaFormats.ExtensionOf(format)}";
        return await store.SaveAsync(stored, head.AsMemory(0, read), content, cancellationToken);
    }
}
