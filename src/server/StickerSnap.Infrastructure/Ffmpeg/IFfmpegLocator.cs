namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>Finds the FFmpeg the server renders with and tells its version.</summary>
internal interface IFfmpegLocator
{
    Task<FfmpegLookup> FindAsync(CancellationToken cancellationToken);
}
