namespace StickerSnap.Core.Links;

/// <summary>
/// What a video by link is before it is downloaded (contract, LinkVideo, artboard 20·3·4): what
/// the site says of it and the file that will come.
/// </summary>
/// <param name="Url">The link to the one video, without `?t=` and the playlist</param>
/// <param name="ThumbnailUrl">The site's preview, https; null when it has none</param>
/// <param name="Bytes">How big the downloaded file is expected to be</param>
/// <param name="Quality">«720p»; for Giphy «MP4» or «WEBP»</param>
/// <param name="Variant">Which of the site's files is downloaded, as the site's adapter names it</param>
/// <param name="Extension">The extension of the file that comes, without the dot: `webm`</param>
public sealed record LinkVideo(
    LinkSite Site,
    string Url,
    string Title,
    double DurationSec,
    string? ThumbnailUrl,
    long Bytes,
    string Quality,
    string Variant,
    string Extension);
