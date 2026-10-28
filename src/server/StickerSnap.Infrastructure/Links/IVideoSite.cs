using StickerSnap.Application.Links;
using StickerSnap.Core.Links;

namespace StickerSnap.Infrastructure.Links;

/// <summary>One site of <see cref="IVideoSites"/>: <see cref="VideoSites"/> hands each link to its own.</summary>
internal interface IVideoSite
{
    LinkSite Site { get; }

    /// <inheritdoc cref="IVideoSites.ProbeAsync"/>
    Task<LinkVideo> ProbeAsync(VideoLink link, CancellationToken cancellationToken);

    /// <inheritdoc cref="IVideoSites.DownloadAsync"/>
    Task<long> DownloadAsync(LinkVideo video, string path, Action<DownloadProgress> progress, CancellationToken cancellationToken);
}

/// <summary>The sites of the import by link, each asked by its own adapter.</summary>
internal sealed class VideoSites(IEnumerable<IVideoSite> sites) : IVideoSites
{
    private readonly Dictionary<LinkSite, IVideoSite> _sites = sites.ToDictionary(site => site.Site);

    public Task<LinkVideo> ProbeAsync(VideoLink link, CancellationToken cancellationToken) =>
        SiteOf(link.Site).ProbeAsync(link, cancellationToken);

    public Task<long> DownloadAsync(
        LinkVideo video, string path, Action<DownloadProgress> progress, CancellationToken cancellationToken) =>
        SiteOf(video.Site).DownloadAsync(video, path, progress, cancellationToken);

    private IVideoSite SiteOf(LinkSite site) =>
        _sites.TryGetValue(site, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"No adapter downloads from {site}");
}
