using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Webm;

/// <summary>
/// <see cref="IStickerHeader"/> over the WEBM the media engine writes: the file is read, its
/// header rewritten by <see cref="WebmDuration"/> and written back at the same length.
/// </summary>
internal sealed class WebmStickerHeader : IStickerHeader
{
    public async Task DeclareDurationAsync(string path, double seconds, CancellationToken cancellationToken)
    {
        var file = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            WebmDuration.Declare(file, seconds);
        }
        catch (FormatException e)
        {
            throw new AppException(ErrorCode.RenderFailed, $"{Path.GetFileName(path)} declares no length to rewrite: {e.Message}", e)
            {
                Log = e.Message,
            };
        }

        await File.WriteAllBytesAsync(path, file, cancellationToken);
    }
}
