namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>Runs `ffmpeg -version` and returns what it printed; null when it would not run.</summary>
internal interface IFfmpegVersionReader
{
    Task<string?> ReadAsync(string executable, CancellationToken cancellationToken);
}
