using StickerSnap.Core.Links;

namespace StickerSnap.Application.Links;

/// <summary>
/// Reads the link, then asks its site. What a site said is kept for <see cref="Remember"/> by
/// the video's link: «Открыть» probes and then downloads the same video, and the site is asked once.
/// </summary>
public sealed class LinkService(IVideoSites sites, TimeProvider clock) : ILinkService
{
    /// <summary>Long enough for the download that follows; short enough for a video changed on the site.</summary>
    public static readonly TimeSpan Remember = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (LinkVideo Video, DateTimeOffset At)> _probed = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<LinkVideo> ProbeAsync(string url, CancellationToken cancellationToken)
    {
        var link = VideoLink.Parse(url);
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            foreach (var old in _probed.Where(pair => now - pair.Value.At >= Remember).Select(pair => pair.Key).ToList())
                _probed.Remove(old);
            if (_probed.TryGetValue(link.Url, out var known)) return known.Video;
        }

        var video = await sites.ProbeAsync(link, cancellationToken);
        lock (_gate) _probed[link.Url] = (video, clock.GetUtcNow());
        return video;
    }
}
