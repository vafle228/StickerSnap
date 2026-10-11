namespace StickerSnap.Application.Ffmpeg;

/// <summary>Finds the FFmpeg the server renders with and tells its version.</summary>
public interface IFfmpegLocator
{
    Task<FfmpegLookup> FindAsync(CancellationToken cancellationToken);
}
