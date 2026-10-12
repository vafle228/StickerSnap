using StickerSnap.Application.Media;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>FFmpeg as the media engine: found by <see cref="IFfmpegLocator"/>.</summary>
internal sealed class FfmpegEngine(IFfmpegLocator locator) : IMediaEngine
{
    public async Task<MediaEngineInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        var lookup = await locator.FindAsync(cancellationToken);
        return new MediaEngineInfo("FFmpeg", lookup.Status, lookup.Version?.Text);
    }
}
